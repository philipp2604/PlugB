using Microsoft.Extensions.Logging;
using PlugB.Abstractions;
using PlugB.Events;
using PlugB.Internal.State;
using PlugB.Internal.Transport;
using PlugB.Models;
using PlugB.Options;

namespace PlugB;

/// <summary>
/// The concrete internal implementation of the PlugB Edge Node Client.
/// </summary>
internal class PlugBClient : IPlugBClient
{
    private readonly PlugBOptions _options;
    private readonly MqttTransport _transport;
    private readonly DeviceRegistry _deviceRegistry;
    private readonly ConnectionStateMachine _stateMachine;
    private readonly PrimaryHostMonitor _hostMonitor;

    /// <inheritdoc />
    public event EventHandler<PlugBConnectionState>? ConnectionStateChanged
    {
        add => _stateMachine.StateChanged += value;
        remove => _stateMachine.StateChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler<PrimaryHostState>? HostStateChanged
    {
        add => _hostMonitor.StateChanged += value;
        remove => _hostMonitor.StateChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler<BufferOverflowInfo>? BufferOverflow
    {
        add => _transport.BufferOverflow += value;
        remove => _transport.BufferOverflow -= value;
    }

    /// <inheritdoc />
    public event EventHandler<int>? HistoricalFlushCompleted
    {
        add => _transport.HistoricalFlushCompleted += value;
        remove => _transport.HistoricalFlushCompleted -= value;
    }

    /// <inheritdoc />
    public PlugBConnectionState ConnectionState => _stateMachine.CurrentState;

    /// <inheritdoc />
    public PrimaryHostState HostState => _hostMonitor.CurrentState;

    public PlugBClient(PlugBOptions options, IReadOnlyList<Metric> nodeBirthMetrics, ILogger? logger)
    {
        _options = options;
        _deviceRegistry = new DeviceRegistry();

        var sequenceManager = new SequenceManager();
        _stateMachine = new ConnectionStateMachine();
        _hostMonitor = new PrimaryHostMonitor(logger);

        _transport = new MqttTransport(
            options,
            sequenceManager,
            _deviceRegistry,
            _stateMachine,
            _hostMonitor,
            nodeBirthMetrics,
            logger);
    }

    /// <inheritdoc />
    public IPlugBDevice CreateDevice(string deviceId)
    {
        var device = new PlugBDevice(deviceId, _options, _transport, _deviceRegistry);
        _deviceRegistry.RegisterDevice(device);
        return device;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _transport.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync();
    }
}