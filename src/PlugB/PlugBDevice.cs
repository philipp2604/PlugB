using PlugB.Abstractions;
using PlugB.Builders;
using PlugB.Internal.Domain;
using PlugB.Internal.State;
using PlugB.Internal.Transport;
using PlugB.Options;
using PlugB.Storage;

namespace PlugB;

internal class PlugBDevice(string deviceId, PlugBOptions options, MqttTransport transport, DeviceRegistry registry) : IPlugBDevice
{
    private readonly string _deviceId = deviceId;
    private readonly PlugBOptions _options = options;
    private readonly MqttTransport _transport = transport;
    private readonly DeviceRegistry _registry = registry;
    private readonly List<Metric> _birthMetrics = [];

    public string DeviceId => _deviceId;

    public void AddBirthMetric(string name, PlugBDataType dataType, object value, ulong? alias = null)
    {
        // no duplicate names per device
        if (_birthMetrics.Exists(m => m.Name == name))
        {
            throw new ArgumentException($"A metric with the name '{name}' already exists on device '{_deviceId}'.");
        }

        var builder = MetricBuilder.Create(name).WithValue(value, dataType);
        if (alias.HasValue)
        {
            builder.WithAlias(alias.Value);
        }

        _birthMetrics.Add(builder.Build());
    }

    public IEnumerable<Metric> GetBirthMetrics()
    {
        return _birthMetrics;
    }

    public Task PublishDataAsync(Metric metric, CancellationToken cancellationToken = default)
    {
        return PublishDataAsync([metric], cancellationToken);
    }

    public async Task PublishDataAsync(IEnumerable<Metric> metrics, CancellationToken cancellationToken = default)
    {
        var topic = TopicGenerator.GetDeviceTopic(_options.GroupId, _options.EdgeNodeId, _deviceId, TopicGenerator.MsgTypeDeviceData);
        await _transport.EnqueuePublishAsync(topic, SparkplugMessageType.DeviceData, metrics);
    }
}