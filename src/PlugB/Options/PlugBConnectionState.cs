namespace PlugB.Options;

/// <summary>
/// Represents the high-level Sparkplug B lifecycle state of the Edge Node.
/// </summary>
public enum PlugBConnectionState
{
    /// <summary>
    /// The client is not connected to any MQTT broker.
    /// </summary>
    Disconnected,

    /// <summary>
    /// The client is connected to the MQTT broker and has subscribed to the STATE topic,
    /// but is waiting for the Primary Host Application to report an ONLINE status before publishing births.
    /// </summary>
    ConnectedAwaitingHost,

    /// <summary>
    /// The client is fully online, meaning the MQTT connection is established and
    /// the Primary Host Application is confirmed to be online. Normal operations are running.
    /// </summary>
    Online,

    /// <summary>
    /// The client is connected to the MQTT broker, but the Primary Host Application has reported an OFFLINE status.
    /// </summary>
    ConnectedHostOffline
}