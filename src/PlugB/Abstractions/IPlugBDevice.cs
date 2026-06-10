using PlugB.Options;

namespace PlugB.Abstractions;

/// <summary>
/// Represents a sub-device attached to the PlugB Sparkplug-B Edge Node.
/// </summary>
public interface IPlugBDevice
{
    /// <summary>
    /// The unique identifier for this device.
    /// </summary>
    string DeviceId { get; }

    /// <summary>
    /// Adds a metric that will be published in the DBIRTH message.
    /// </summary>
    void AddBirthMetric(string name, PlugBDataType dataType, object value, ulong? alias = null);

    /// <summary>
    /// Publishes a single runtime data metric (DDATA) for this device.
    /// </summary>
    Task PublishDataAsync(Metric metric, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes multiple runtime data metrics (DDATA) for this device.
    /// </summary>
    Task PublishDataAsync(IEnumerable<Metric> metrics, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all registered birth metrics for this device.
    /// Primarily used internally during the connection lifecycle.
    /// </summary>
    IEnumerable<Metric> GetBirthMetrics();
}