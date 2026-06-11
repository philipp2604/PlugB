using PlugB.Options;

namespace PlugB.Abstractions;

/// <summary>
/// The main client to interact with the PlugB Sparkplug-B Edge Node.
/// Completely abstracts the underlying MQTT connection and sequence management.
/// </summary>
public interface IPlugBClient : IAsyncDisposable
{
    /// <summary>
    /// Triggered when the Edge Node's high-level connection state changes.
    /// </summary>
    event EventHandler<PlugBConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// Triggered when the Primary Host Application state changes (Online/Offline).
    /// </summary>
    event EventHandler<PrimaryHostState>? HostStateChanged;

    /// <summary>
    /// Triggered when the Store-and-Forward buffer overflows and metrics are evicted.
    /// </summary>
    event EventHandler<BufferOverflowInfo>? BufferOverflow;

    /// <summary>
    /// Triggered when the Store-and-Forward buffer has successfully flushed historical data after reconnecting.
    /// </summary>
    event EventHandler<int>? HistoricalFlushCompleted;

    /// <summary>
    /// Gets the current high-level lifecycle state of the Edge Node.
    /// </summary>
    PlugBConnectionState ConnectionState { get; }

    /// <summary>
    /// Gets the current known state of the Primary Host Application.
    /// </summary>
    PrimaryHostState HostState { get; }

    /// <summary>
    /// Creates and registers a new sub-device. 
    /// The client will automatically manage the DBIRTH lifecycle for this device.
    /// </summary>
    /// <param name="deviceId">The unique identifier of the device.</param>
    /// <returns>An interface representing the configured device.</returns>
    IPlugBDevice CreateDevice(string deviceId);

    /// <summary>
    /// Starts the lifecycle: Connects to the broker, sets up the LWT, 
    /// waits for the Primary Host (if configured), and publishes birth certificates.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the startup process.</param>
    Task StartAsync(CancellationToken cancellationToken = default);
}