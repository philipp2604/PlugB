using Microsoft.Extensions.Logging;
using PlugB.Abstractions;
using PlugB.Internal.State;
using PlugB.Internal.Transport;
using PlugB.Options;

namespace PlugB;

internal class PlugBClient : IPlugBClient
{
    private readonly PlugBOptions _options;
    private readonly MqttTransport _transport;
    private readonly DeviceRegistry _deviceRegistry;

    public PlugBClient(PlugBOptions options, IReadOnlyList<Metric> nodeBirthMetrics, ILogger? logger)
    {
        _options = options;
        _deviceRegistry = new DeviceRegistry();

        // one SequenceManager per node
        var sequenceManager = new SequenceManager();

        _transport = new MqttTransport(options, sequenceManager, _deviceRegistry, nodeBirthMetrics, logger);
    }

    public IPlugBDevice CreateDevice(string deviceId)
    {
        var device = new PlugBDevice(deviceId, _options, _transport, _deviceRegistry);
        _deviceRegistry.RegisterDevice(device); // throws ArgumentException if ID already exists
        return device;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _transport.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync();
    }
}