using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MQTTnet;
using PlugB.Builders;
using PlugB.Events;
using PlugB.Internal.Mapping;
using PlugB.Internal.State;
using PlugB.Models;
using PlugB.Options;

namespace PlugB.Internal.Transport;

internal class HostMqttTransport : IAsyncDisposable
{
    private readonly PlugBHostOptions _options;
    private readonly HostNodeRegistry _registry;
    private readonly HostSequenceTracker _tracker;
    private readonly ServerSelector _serverSelector;
    private readonly ILogger? _logger;
    private readonly IMqttClient _client;

    private bool _isDisposed;
    private CancellationTokenSource? _cts;

    public event EventHandler<NodeBirthEventArgs>? NodeBirth;
    public event EventHandler<DeviceBirthEventArgs>? DeviceBirth;
    public event EventHandler<DataChangedEventArgs>? DataChanged;
    public event EventHandler<NodeDeathEventArgs>? NodeDeath;
    public event EventHandler<DeviceDeathEventArgs>? DeviceDeath;
    public event EventHandler<RebirthRequestedEventArgs>? RebirthRequested;
    public event EventHandler<DecodeFailedEventArgs>? DecodeFailed;
    public event EventHandler<bool>? ConnectionChanged;

    public HostMqttTransport(
        PlugBHostOptions options,
        HostNodeRegistry registry,
        HostSequenceTracker tracker,
        ILogger? logger,
        Func<IMqttClient>? clientFactory = null)
    {
        _options = options;
        _registry = registry;
        _tracker = tracker;
        _serverSelector = new ServerSelector(options.Servers);
        _logger = logger;
        _client = clientFactory != null ? clientFactory() : new MqttClientFactory().CreateMqttClient();

        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = new CancellationTokenSource();
        await ConnectInternalAsync(cancellationToken);
    }

    public bool IsConnected => _client.IsConnected;

    private async Task ConnectInternalAsync(CancellationToken ct)
    {
        try
        {
            var currentServer = _serverSelector.GetCurrent();
            long stateTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var stateTopic = TopicGenerator.GetStateTopic(_options.HostId);
            var willPayload = StateSerializer.Serialize(online: false, stateTimestamp);

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(currentServer.Address, currentServer.Port)
                .WithClientId(_options.ClientId)
                .WithWillTopic(stateTopic)
                .WithWillPayload(willPayload)
                .WithWillQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .WithWillRetain(true);

            if (!string.IsNullOrEmpty(_options.Username))
                optionsBuilder.WithCredentials(_options.Username, _options.Password);

            if (currentServer.UseTls)
                optionsBuilder.WithTlsOptions(o => o.UseTls());

            await _client.ConnectAsync(optionsBuilder.Build(), ct);

            if (_logger?.IsEnabled(LogLevel.Information) ?? false)
                _logger?.LogInformation("Host connected to MQTT broker {Address}:{Port}.", currentServer.Address, currentServer.Port);

            // Publish BIRTH STATE immediately with the exact same timestamp
            var birthPayload = StateSerializer.Serialize(online: true, stateTimestamp);
            var stateMsg = new MqttApplicationMessageBuilder()
                .WithTopic(stateTopic)
                .WithPayload(birthPayload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(true)
                .Build();

            await _client.PublishAsync(stateMsg, ct);

            // Subscriptions
            var subBuilder = new MqttClientSubscribeOptionsBuilder();
            if (_options.GroupFilters.Count == 0)
            {
                subBuilder.WithTopicFilter(f => f.WithTopic($"{TopicGenerator.Prefix}/#").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce));
            }
            else
            {
                foreach (var group in _options.GroupFilters)
                {
                    subBuilder.WithTopicFilter(f => f.WithTopic($"{TopicGenerator.Prefix}/{group}/#").WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce));
                }
            }

            await _client.SubscribeAsync(subBuilder.Build(), ct);
            ConnectionChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect Host to MQTT broker.");
            throw;
        }
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        if (_isDisposed) return;

        _logger?.LogWarning("Host MQTT connection lost. Reconnecting in 5 seconds...");
        ConnectionChanged?.Invoke(this, false);
        _serverSelector.MoveNext();

        while (!_isDisposed && !_client.IsConnected)
        {
            try
            {
                await Task.Delay(5000, _cts?.Token ?? CancellationToken.None);
                await ConnectInternalAsync(_cts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger?.LogDebug(ex, "Host reconnect failed."); }
        }
    }

    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        var topic = arg.ApplicationMessage.Topic;
        var parsed = TopicParser.Parse(topic);

        if (parsed == null || parsed.Kind == SparkplugMessageKind.State)
        {
            return; // Ignore invalid topics and STATE messages
        }

        Com.Cirruslink.Sparkplug.Protobuf.Payload payload;
        try
        {
            payload = PayloadDecoder.Parse(arg.ApplicationMessage.Payload);
        }
        catch (Exception ex)
        {
            DecodeFailed?.Invoke(this, new DecodeFailedEventArgs(topic, ex));
            return;
        }

        var metrics = PayloadDecoder.ToMetrics(payload);

        if (parsed.Kind == SparkplugMessageKind.NDeath)
        {
            var bdSeqMetric = metrics.FirstOrDefault(m => m.Name == "bdSeq");
            if (bdSeqMetric != null)
            {
                ulong bdSeq = bdSeqMetric.Value is ulong u ? u : (ulong)Convert.ToInt64(bdSeqMetric.Value);
                if (_tracker.ValidateNDeath(parsed.GroupId!, parsed.EdgeNodeId!, bdSeq) == SequenceValidationResult.Valid)
                {
                    _registry.ProcessNDeath(parsed.GroupId!, parsed.EdgeNodeId!);
                    NodeDeath?.Invoke(this, new NodeDeathEventArgs(parsed.GroupId!, parsed.EdgeNodeId!));
                }
            }
            return;
        }

        if (parsed.Kind == SparkplugMessageKind.NBirth)
        {
            var bdSeqMetric = metrics.FirstOrDefault(m => m.Name == "bdSeq");
            ulong bdSeq = bdSeqMetric != null ? (ulong)Convert.ToInt64(bdSeqMetric.Value) : 0;

            _tracker.ProcessNBirth(parsed.GroupId!, parsed.EdgeNodeId!, bdSeq);
            var node = _registry.ProcessNBirth(parsed.GroupId!, parsed.EdgeNodeId!, bdSeq, metrics);

            NodeBirth?.Invoke(this, new NodeBirthEventArgs(parsed.GroupId!, parsed.EdgeNodeId!, node.Metrics));
            return;
        }

        var seqResult = _tracker.ValidateSequence(parsed.GroupId!, parsed.EdgeNodeId!, payload.Seq);
        if (seqResult != SequenceValidationResult.Valid)
        {
            HandleRebirthRequest(parsed.GroupId!, parsed.EdgeNodeId!, "Sequence Gap or Data Before Birth");
            return; // Discard out-of-order/invalid data
        }

        DataProcessingResult processResult = DataProcessingResult.Success;

        if (parsed.Kind == SparkplugMessageKind.NData)
        {
            var (res, node) = _registry.ProcessNData(parsed.GroupId!, parsed.EdgeNodeId!, metrics);
            processResult = res;
            if (res == DataProcessingResult.Success)
                DataChanged?.Invoke(this, new DataChangedEventArgs(parsed.GroupId!, parsed.EdgeNodeId!, null, metrics));
        }
        else if (parsed.Kind == SparkplugMessageKind.DBirth)
        {
            var dev = _registry.ProcessDBirth(parsed.GroupId!, parsed.EdgeNodeId!, parsed.DeviceId!, metrics);
            if (dev != null)
                DeviceBirth?.Invoke(this, new DeviceBirthEventArgs(parsed.GroupId!, parsed.EdgeNodeId!, parsed.DeviceId!, dev.Metrics));
        }
        else if (parsed.Kind == SparkplugMessageKind.DData)
        {
            var (res, dev) = _registry.ProcessDData(parsed.GroupId!, parsed.EdgeNodeId!, parsed.DeviceId!, metrics);
            processResult = res;
            if (res == DataProcessingResult.Success)
                DataChanged?.Invoke(this, new DataChangedEventArgs(parsed.GroupId!, parsed.EdgeNodeId!, parsed.DeviceId!, metrics));
        }
        else if (parsed.Kind == SparkplugMessageKind.DDeath)
        {
            var dev = _registry.ProcessDDeath(parsed.GroupId!, parsed.EdgeNodeId!, parsed.DeviceId!);
            if (dev != null)
                DeviceDeath?.Invoke(this, new DeviceDeathEventArgs(parsed.GroupId!, parsed.EdgeNodeId!, parsed.DeviceId!));
        }

        if (processResult == DataProcessingResult.UnknownAlias)
        {
            HandleRebirthRequest(parsed.GroupId!, parsed.EdgeNodeId!, "Unknown Alias");
        }
    }

    private void HandleRebirthRequest(string groupId, string edgeNodeId, string reason)
    {
        if (_options.RebirthOnGap)
        {
            _logger?.LogWarning("Requesting Rebirth for {GroupId}/{EdgeNodeId}. Reason: {Reason}", groupId, edgeNodeId, reason);
            _tracker.MarkUnborn(groupId, edgeNodeId);

            // Fire-and-forget the command to prevent blocking the MQTT consumer thread
            _ = SendRebirthCommandAsync(groupId, edgeNodeId, _cts?.Token ?? CancellationToken.None);
        }

        RebirthRequested?.Invoke(this, new RebirthRequestedEventArgs(groupId, edgeNodeId));
    }

    public async Task SendRebirthCommandAsync(string groupId, string edgeNodeId, CancellationToken ct)
    {
        var metric = MetricBuilder.Create("Node Control/Rebirth").WithValue(true, PlugBDataType.Boolean).Build();
        await PublishCommandAsync(TopicGenerator.GetNodeTopic(groupId, edgeNodeId, TopicGenerator.MsgTypeNodeCommand), [metric], ct);
    }

    public async Task PublishCommandAsync(string topic, IEnumerable<Metric> metrics, CancellationToken ct)
    {
        if (!_client.IsConnected) throw new InvalidOperationException("Host is not connected.");

        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = PayloadBuilder.Build(metrics, seq: null, (ulong)ts); // Commands have no sequence

        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.ToByteArray())
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
            .Build();

        await _client.PublishAsync(msg, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        _cts?.Cancel();

        if (_client.IsConnected)
        {
            try
            {
                var stateTopic = TopicGenerator.GetStateTopic(_options.HostId);
                var offlinePayload = StateSerializer.Serialize(online: false, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                var stateMsg = new MqttApplicationMessageBuilder()
                    .WithTopic(stateTopic)
                    .WithPayload(offlinePayload)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(true)
                    .Build();

                await _client.PublishAsync(stateMsg);
                await _client.DisconnectAsync();
            }
            catch { }
        }

        _client.Dispose();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}