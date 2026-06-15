using PlugB.Events;
using PlugB.Models;

namespace PlugB.Abstractions;

/// <summary>
/// The main client interface to interact with the PlugB Sparkplug-B Host Application.
/// It subscribes to metrics, builds a namespace registry, and sends commands.
/// </summary>
public interface IPlugBHost : IAsyncDisposable
{
    event EventHandler<NodeBirthEventArgs>? NodeBirth;
    event EventHandler<NodeDeathEventArgs>? NodeDeath;
    event EventHandler<DeviceBirthEventArgs>? DeviceBirth;
    event EventHandler<DeviceDeathEventArgs>? DeviceDeath;
    event EventHandler<DataChangedEventArgs>? DataChanged;
    event EventHandler<RebirthRequestedEventArgs>? RebirthRequested;
    event EventHandler<DecodeFailedEventArgs>? DecodeFailed;
    event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// Gets a value indicating whether the Host is currently connected to the MQTT broker.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets a live snapshot of all registered nodes and their devices/metrics.
    /// </summary>
    IReadOnlyCollection<HostNode> Nodes { get; }

    /// <summary>
    /// Starts the lifecycle: Connects to the broker, publishes the STATE Will and Birth, and subscribes to topics.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a Node Control command requesting the specified Edge Node to publish a new NBIRTH.
    /// </summary>
    Task RequestRebirthAsync(string groupId, string edgeNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a custom command (NCMD) to an Edge Node.
    /// </summary>
    Task SendNodeCommandAsync(string groupId, string edgeNodeId, IEnumerable<Metric> metrics, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a custom command (DCMD) to a specific Device attached to an Edge Node.
    /// </summary>
    Task SendDeviceCommandAsync(string groupId, string edgeNodeId, string deviceId, IEnumerable<Metric> metrics, CancellationToken cancellationToken = default);
}