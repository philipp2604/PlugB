namespace PlugB.Abstractions;

/// <summary>
/// The main client to interact with the PlugB Sparkplug-B Edge Node.
/// Completely abstracts the underlying MQTT connection and sequence management.
/// </summary>
public interface IPlugBClient : IAsyncDisposable
{
    /// <summary>
    /// Creates and registers a new sub-device. 
    /// The client will automatically manage the DBIRTH lifecycle for this device.
    /// </summary>
    /// <param name="deviceId">The unique identifier of the device.</param>
    IPlugBDevice CreateDevice(string deviceId);

    /// <summary>
    /// Connects to the MQTT broker, sets the NDEATH (Last Will), and publishes the NBIRTH.
    /// Following that, DBIRTHs for all registered devices will be published automatically.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}