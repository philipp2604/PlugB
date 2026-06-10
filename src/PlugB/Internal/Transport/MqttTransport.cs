using System.Threading.Channels;
using MQTTnet;
using Microsoft.Extensions.Logging;
using Google.Protobuf;
using PlugB.Options;
using PlugB.Exceptions;
using PlugB.Internal.Domain;
using PlugB.Internal.State;
using PlugB.Builders;
using ProtoPayload = Com.Cirruslink.Sparkplug.Protobuf.Payload;

namespace PlugB.Internal.Transport;

internal class MqttTransport : IAsyncDisposable
{
    private readonly PlugBOptions _options;
    private readonly SequenceManager _sequenceManager;
    private readonly DeviceRegistry _deviceRegistry;
    private readonly IReadOnlyList<Metric> _nodeBirthMetrics;
    private readonly ILogger? _logger;

    private readonly IMqttClient _client;
    private readonly Channel<PublishCommand> _publishChannel;
    private CancellationTokenSource? _cts;

    private bool _isDisposed;
    private ulong _currentBdSeq;
    private Task? _consumerTask;

    public MqttTransport(
        PlugBOptions options,
        SequenceManager sequenceManager,
        DeviceRegistry deviceRegistry,
        IReadOnlyList<Metric> nodeBirthMetrics,
        ILogger? logger = null)
    {
        _options = options;
        _sequenceManager = sequenceManager;
        _deviceRegistry = deviceRegistry;
        _nodeBirthMetrics = nodeBirthMetrics;
        _logger = logger;

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        // Single-Reader/Multi-Writer -> strictly serialized publishing
        _publishChannel = Channel.CreateUnbounded<PublishCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = new CancellationTokenSource();
        _consumerTask = Task.Run(() => ProcessPublishQueueAsync(_cts.Token), CancellationToken.None);

        await ConnectInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Enqueues a message to be published. Will be processed by the single consumer loop.
    /// </summary>
    public void EnqueuePublish(string topic, SparkplugMessageType type, IEnumerable<Metric> metrics)
    {
        if (!_client.IsConnected)
        {
            throw new PlugBNotConnectedException();
        }

        _publishChannel.Writer.TryWrite(new PublishCommand(topic, type, metrics));
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        try
        {
            // increment bdSeq BEFORE connect and use it for the LWT (NDEATH)
            _currentBdSeq = _sequenceManager.NextBdSeq();

            var nDeathTopic = TopicGenerator.GetNodeTopic(_options.GroupId, _options.EdgeNodeId, TopicGenerator.MsgTypeNodeDeath);
            var bdSeqMetric = MetricBuilder.Create("bdSeq").WithValue(_currentBdSeq, PlugBDataType.UInt64).Build();

            // NDEATH has NO seq number
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var nDeathPayload = PayloadBuilder.Build([bdSeqMetric], seq: null, timestampMs: (ulong)timestamp);

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_options.BrokerAddress, _options.BrokerPort)
                .WithClientId(_options.ClientId)
                .WithWillTopic(nDeathTopic)
                .WithWillPayload(nDeathPayload.ToByteArray())
                .WithWillQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                .WithWillRetain(false);

            if (!string.IsNullOrEmpty(_options.Username))
            {
                optionsBuilder.WithCredentials(_options.Username, _options.Password);
            }

            if (_options.UseTls)
            {
                optionsBuilder.WithTlsOptions(o => o.UseTls());
            }

            await _client.ConnectAsync(optionsBuilder.Build(), ct);
            _logger?.LogInformation("PlugB Edge Node connected to MQTT broker.");

            // Subscribe to NCMD for Rebirth commands
            var ncmdTopic = TopicGenerator.GetNodeTopic(_options.GroupId, _options.EdgeNodeId, TopicGenerator.MsgTypeNodeCommand);
            var subOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(ncmdTopic))
                .Build();
            await _client.SubscribeAsync(subOptions, ct);

            // NBIRTH sent directly after connect MUST share the identical bdSeq.
            TriggerNodeBirth();

            // DBIRTHs are sent AFTER NBIRTH
            TriggerAllDeviceBirths();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to MQTT broker.");
            throw new PlugBConnectionException("Initial MQTT connection failed.", ex);
        }
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        if (_isDisposed) return;

        _logger?.LogWarning("MQTT connection lost. Attempting reconnect in 5 seconds...");

        // mark all devices to require a new DBIRTH upon next connect
        _deviceRegistry.MarkAllBirthsAsUnsent();

        while (!_isDisposed && !_client.IsConnected)
        {
            try
            {
                await Task.Delay(5000, _cts?.Token ?? CancellationToken.None);
                await ConnectInternalAsync(_cts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Reconnect attempt failed.");
            }
        }
    }

    private Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        var topic = arg.ApplicationMessage.Topic;

        // handle NCMD (Node Command) -> Check for Rebirth
        if (topic.Contains($"/{TopicGenerator.MsgTypeNodeCommand}/"))
        {
            try
            {
                var payload = ProtoPayload.Parser.ParseFrom(arg.ApplicationMessage.Payload);
                var rebirthMetric = payload.Metric.FirstOrDefault(m => m.Name == "Node Control/Rebirth");

                // if Rebirth = true is received, resend NBIRTH and all DBIRTHs
                if (rebirthMetric != null && rebirthMetric.BooleanValue)
                {
                    _logger?.LogInformation("Rebirth requested via NCMD. Republishing birth certificates.");
                    TriggerNodeBirth();
                    TriggerAllDeviceBirths();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to process incoming NCMD message.");
            }
        }

        return Task.CompletedTask;
    }

    private void TriggerNodeBirth()
    {
        var bdSeqMetric = MetricBuilder.Create("bdSeq").WithValue(_currentBdSeq, PlugBDataType.UInt64).Build();
        var allNodeMetrics = new List<Metric> { bdSeqMetric };
        allNodeMetrics.AddRange(_nodeBirthMetrics);

        var topic = TopicGenerator.GetNodeTopic(_options.GroupId, _options.EdgeNodeId, TopicGenerator.MsgTypeNodeBirth);
        EnqueuePublish(topic, SparkplugMessageType.NodeBirth, allNodeMetrics);
    }

    private void TriggerAllDeviceBirths()
    {
        foreach (var device in _deviceRegistry.GetAllDevices())
        {
            var topic = TopicGenerator.GetDeviceTopic(_options.GroupId, _options.EdgeNodeId, device.DeviceId, TopicGenerator.MsgTypeDeviceBirth);
            EnqueuePublish(topic, SparkplugMessageType.DeviceBirth, device.GetBirthMetrics());
            _deviceRegistry.MarkBirthSent(device.DeviceId);
        }
    }

    /// <summary>
    /// The single, serialized consumer loop for publishing. Guarantees Rule S1 & §5.
    /// </summary>
    private async Task ProcessPublishQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var cmd in _publishChannel.Reader.ReadAllAsync(ct))
            {
                if (!_client.IsConnected) continue; // drop message if not connected (No Offline Queuing)

                ulong seq;
                if (cmd.Type == SparkplugMessageType.NodeBirth)
                {
                    // NBIRTH forces a seq reset to 0
                    _sequenceManager.ResetSeq();
                    seq = _sequenceManager.NextSeq(); // which is 0
                }
                else
                {
                    // every subsequent message increments seq
                    seq = _sequenceManager.NextSeq();
                }

                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var payload = PayloadBuilder.Build(cmd.Metrics, seq, (ulong)timestamp);

                var mqttMsg = new MqttApplicationMessageBuilder()
                    .WithTopic(cmd.Topic)
                    .WithPayload(payload.ToByteArray())
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                    .Build();

                await _client.PublishAsync(mqttMsg, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Fatal error in publish pipeline.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        _cts?.Cancel();
        _publishChannel.Writer.TryComplete();

        if (_consumerTask != null)
        {
            await Task.WhenAny(_consumerTask, Task.Delay(1000));
        }

        if (_client.IsConnected)
        {
            // relying on the MQTT Broker to publish the LWT upon disconnect
            await _client.DisconnectAsync();
        }

        _client.Dispose();
        _cts?.Dispose();
    }
}