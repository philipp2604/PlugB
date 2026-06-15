using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MQTTnet;
using PlugB.Builders;
using PlugB.Events;
using PlugB.Exceptions;
using PlugB.Internal.Mapping;
using PlugB.Internal.State;
using PlugB.Models;
using PlugB.Options;
using PlugB.Storage;
using System.Text;
using System.Threading.Channels;
using ProtoPayload = Com.Cirruslink.Sparkplug.Protobuf.Payload;

namespace PlugB.Internal.Transport;

/// <summary>
/// Manages the low-level MQTT connection, reconnection logic, publish pipeline, and Sparkplug B lifecycle events.
/// </summary>
internal class ClientMqttTransport : IAsyncDisposable
{
    private readonly PlugBClientOptions _options;
    private readonly SequenceManager _sequenceManager;
    private readonly DeviceRegistry _deviceRegistry;
    private readonly ConnectionStateMachine _stateMachine;
    private readonly PrimaryHostMonitor _hostMonitor;
    private readonly ServerSelector _serverSelector;
    private readonly IReadOnlyList<Metric> _nodeBirthMetrics;
    private readonly ILogger? _logger;

    private readonly IMqttClient _client;
    private readonly Channel<PublishCommand> _publishChannel;
    private CancellationTokenSource? _cts;

    private bool _isDisposed;
    private ulong _currentBdSeq;
    private Task? _consumerTask;

    public event EventHandler<BufferOverflowInfo>? BufferOverflow;

    public event EventHandler<int>? HistoricalFlushCompleted;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientMqttTransport"/> class.
    /// Allows injecting a custom client factory for testing purposes.
    /// </summary>
    public ClientMqttTransport(
        PlugBClientOptions options,
        SequenceManager sequenceManager,
        DeviceRegistry deviceRegistry,
        ConnectionStateMachine stateMachine,
        PrimaryHostMonitor hostMonitor,
        IReadOnlyList<Metric> nodeBirthMetrics,
        ILogger? logger = null,
        Func<IMqttClient>? clientFactory = null)
    {
        _options = options;
        _sequenceManager = sequenceManager;
        _deviceRegistry = deviceRegistry;
        _stateMachine = stateMachine;
        _hostMonitor = hostMonitor;
        _serverSelector = new ServerSelector(options.Servers);
        _nodeBirthMetrics = nodeBirthMetrics;
        _logger = logger;

        _client = clientFactory != null ? clientFactory() : new MqttClientFactory().CreateMqttClient();

        _publishChannel = Channel.CreateUnbounded<PublishCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;

        if (_options.StoreAndForward.Enabled && _options.StoreAndForward.Store != null)
        {
            _options.StoreAndForward.Store.BufferOverflow += (s, e) => BufferOverflow?.Invoke(this, e);
        }
    }

    /// <summary>
    /// Initiates the connection process and starts the internal consumer pipeline.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = new CancellationTokenSource();
        _consumerTask = Task.Run(() => ProcessPublishQueueAsync(_cts.Token), CancellationToken.None);

        await ConnectInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Enqueues a payload for publishing. Validates gating rules before accepting data.
    /// </summary>
    public async ValueTask EnqueuePublishAsync(string topic, SparkplugMessageType type, IEnumerable<Metric> metrics)
    {
        if (_stateMachine.CurrentState != PlugBConnectionState.Online)
        {
            if (!_options.StoreAndForward.Enabled)
            {
                throw new PlugBNotConnectedException("Client is not ONLINE and Store-and-Forward is disabled.");
            }

            if (_options.StoreAndForward.Eviction == EvictionPolicy.RejectNew && _options.StoreAndForward.Store != null)
            {
                int count = await _options.StoreAndForward.Store.CountAsync(CancellationToken.None);
                if (count >= _options.StoreAndForward.Capacity)
                {
                    throw new InvalidOperationException("Store-and-Forward buffer is full (RejectNew policy).");
                }
            }
        }

        _publishChannel.Writer.TryWrite(new PublishCommand(topic, type, metrics));
    }

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        try
        {
            var currentServer = _serverSelector.GetCurrent();
            _currentBdSeq = _sequenceManager.NextBdSeq();

            var nDeathTopic = TopicGenerator.GetNodeTopic(_options.GroupId, _options.EdgeNodeId, TopicGenerator.MsgTypeNodeDeath);
            var bdSeqMetric = MetricBuilder.Create("bdSeq").WithValue(_currentBdSeq, PlugBDataType.UInt64).Build();

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var nDeathPayload = PayloadBuilder.Build([bdSeqMetric], seq: null, timestampMs: (ulong)timestamp);

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(currentServer.Address, currentServer.Port)
                .WithClientId(_options.ClientId)
                .WithWillTopic(nDeathTopic)
                .WithWillPayload(nDeathPayload.ToByteArray())
                .WithWillQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                .WithWillRetain(false);

            if (!string.IsNullOrEmpty(_options.Username))
                optionsBuilder.WithCredentials(_options.Username, _options.Password);

            if (currentServer.UseTls)
                optionsBuilder.WithTlsOptions(o => o.UseTls());

            await _client.ConnectAsync(optionsBuilder.Build(), ct);

            if (_logger?.IsEnabled(LogLevel.Information) ?? false)
                _logger?.LogInformation("PlugB connected to MQTT broker {Address}:{Port}.", currentServer.Address, currentServer.Port);

            var ncmdTopic = TopicGenerator.GetNodeTopic(_options.GroupId, _options.EdgeNodeId, TopicGenerator.MsgTypeNodeCommand);
            await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter(ncmdTopic).Build(), ct);

            if (!string.IsNullOrEmpty(_options.PrimaryHostId))
            {
                var stateTopic = TopicGenerator.GetStateTopic(_options.PrimaryHostId);
                await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic(stateTopic).WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce))
                    .Build(), ct);

                if (_logger?.IsEnabled(LogLevel.Information) ?? false)
                    _logger?.LogInformation("Awaiting Primary Host '{HostId}' to become ONLINE before publishing births...", _options.PrimaryHostId);

                _stateMachine.TransitionTo(PlugBConnectionState.ConnectedAwaitingHost);

                _ = Task.Run(() => AwaitPrimaryHostAsync(_cts!.Token), CancellationToken.None);
            }
            else
            {
                _stateMachine.TransitionTo(PlugBConnectionState.Online);
                ExecuteBirthSequence();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to MQTT broker.");
            throw new PlugBConnectionException("Initial MQTT connection failed.", ex);
        }
    }

    private async Task AwaitPrimaryHostAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_options.PrimaryHostWaitTimeout, ct);
            if (_stateMachine.CurrentState == PlugBConnectionState.ConnectedAwaitingHost)
            {
                _logger?.LogWarning("Primary Host wait timeout expired. Triggering failover to next server.");
                await FailoverAsync();
            }
        }
        catch (TaskCanceledException) { }
    }

    private async Task FailoverAsync()
    {
        if (_client.IsConnected)
        {
            var disconnectOptions = new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                .Build();
            await _client.DisconnectAsync(disconnectOptions);
        }
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        if (_isDisposed) return;

        _logger?.LogWarning("MQTT connection lost. Moving to next server and attempting reconnect in 5 seconds...");

        _stateMachine.TransitionTo(PlugBConnectionState.Disconnected);
        _serverSelector.MoveNext();
        _hostMonitor.Reset();
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

    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        var topic = arg.ApplicationMessage.Topic;

        if (!string.IsNullOrEmpty(_options.PrimaryHostId) && topic == TopicGenerator.GetStateTopic(_options.PrimaryHostId))
        {
            var payloadString = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);
            var stateMsg = StateParser.Parse(payloadString, _logger);

            if (stateMsg != null)
            {
                bool changed = _hostMonitor.ProcessStateMessage(stateMsg);

                if (changed || _stateMachine.CurrentState == PlugBConnectionState.ConnectedAwaitingHost)
                {
                    if (_hostMonitor.CurrentState.Online)
                    {
                        _stateMachine.TransitionTo(PlugBConnectionState.Online);
                        ExecuteBirthSequence();
                    }
                    else if (_stateMachine.CurrentState == PlugBConnectionState.Online)
                    {
                        _stateMachine.TransitionTo(PlugBConnectionState.ConnectedHostOffline);
                        _ = Task.Run(() => FailoverAsync());
                    }
                }
            }
            return;
        }

        if (topic.Contains($"/{TopicGenerator.MsgTypeNodeCommand}/"))
        {
            try
            {
                var payload = ProtoPayload.Parser.ParseFrom(arg.ApplicationMessage.Payload);
                var rebirthMetric = payload.Metric.FirstOrDefault(m => m.Name == "Node Control/Rebirth");

                if (rebirthMetric != null && rebirthMetric.BooleanValue && _stateMachine.CurrentState == PlugBConnectionState.Online)
                {
                    _logger?.LogInformation("Rebirth requested via NCMD. Republishing birth certificates.");
                    ExecuteBirthSequence();
                }
            }
            catch (Exception ex) { _logger?.LogError(ex, "Failed to process incoming NCMD message."); }
        }
    }

    private void ExecuteBirthSequence()
    {
        var bdSeqMetric = MetricBuilder.Create("bdSeq").WithValue(_currentBdSeq, PlugBDataType.UInt64).Build();
        var allNodeMetrics = new List<Metric> { bdSeqMetric };
        allNodeMetrics.AddRange(_nodeBirthMetrics);

        var nodeTopic = TopicGenerator.GetNodeTopic(_options.GroupId, _options.EdgeNodeId, TopicGenerator.MsgTypeNodeBirth);
        _publishChannel.Writer.TryWrite(new PublishCommand(nodeTopic, SparkplugMessageType.NodeBirth, allNodeMetrics));

        foreach (var device in _deviceRegistry.GetAllDevices())
        {
            var devTopic = TopicGenerator.GetDeviceTopic(_options.GroupId, _options.EdgeNodeId, device.DeviceId, TopicGenerator.MsgTypeDeviceBirth);
            _publishChannel.Writer.TryWrite(new PublishCommand(devTopic, SparkplugMessageType.DeviceBirth, device.GetBirthMetrics()));
            _deviceRegistry.MarkBirthSent(device.DeviceId);
        }

        // trigger flush AFTER NBIRTH and DBIRTHs
        if (_options.StoreAndForward.Enabled)
        {
            _publishChannel.Writer.TryWrite(new PublishCommand("FLUSH", SparkplugMessageType.InternalFlush, []));
        }
    }

    private async Task ProcessPublishQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var cmd in _publishChannel.Reader.ReadAllAsync(ct))
            {
                if (cmd.Type == SparkplugMessageType.InternalFlush)
                {
                    await FlushStoreAsync(ct);
                    continue;
                }

                if (_stateMachine.CurrentState != PlugBConnectionState.Online)
                {
                    if (_options.StoreAndForward.Enabled && _options.StoreAndForward.Store != null)
                    {
                        foreach (var metric in cmd.Metrics)
                        {
                            try
                            {
                                await _options.StoreAndForward.Store.EnqueueAsync(new ForwardEntry(cmd.Topic, cmd.Type, metric), ct);
                            }
                            catch (InvalidOperationException) { }
                        }
                    }
                    continue;
                }

                ulong seq = cmd.Type == SparkplugMessageType.NodeBirth ? _sequenceManager.ResetAndGetSeq() : _sequenceManager.NextSeq();
                var payload = PayloadBuilder.Build(cmd.Metrics, seq, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                var mqttMsg = new MqttApplicationMessageBuilder()
                    .WithTopic(cmd.Topic)
                    .WithPayload(payload.ToByteArray())
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                    .Build();

                await _client.PublishAsync(mqttMsg, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger?.LogCritical(ex, "Fatal error in publish pipeline."); }
    }

    private async Task FlushStoreAsync(CancellationToken ct)
    {
        if (_options.StoreAndForward.Store == null) return;

        int count = 0;
        await foreach (var entry in _options.StoreAndForward.Store.DrainAsync(ct))
        {
            if (_stateMachine.CurrentState != PlugBConnectionState.Online) break; // Abort if connection lost

            var histMetric = entry.Metric with { IsHistorical = true };
            ulong seq = _sequenceManager.NextSeq();

            var payload = PayloadBuilder.Build([histMetric], seq, (ulong)histMetric.TimestampMilliseconds);

            var mqttMsg = new MqttApplicationMessageBuilder()
                .WithTopic(entry.TargetTopic)
                .WithPayload(payload.ToByteArray())
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            await _client.PublishAsync(mqttMsg, ct);
            count++;
        }

        if (count > 0)
        {
            if (_logger?.IsEnabled(LogLevel.Information) ?? false)
                _logger?.LogInformation("Historical flush completed. Sent {Count} messages.", count);
            HistoricalFlushCompleted?.Invoke(this, count);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        _cts?.Cancel();
        _publishChannel.Writer.TryComplete();

        if (_consumerTask != null) await Task.WhenAny(_consumerTask, Task.Delay(1000));

        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }

        _client.Dispose();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}