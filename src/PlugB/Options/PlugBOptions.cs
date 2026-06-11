namespace PlugB.Options;

/// <summary>
/// Configuration options for the PlugB Sparkplug-B Edge Node Client.
/// </summary>
public record PlugBOptions
{
    /// <summary>
    /// The list of MQTT servers to connect to. Supports multiple servers for failover.
    /// </summary>
    public required IReadOnlyList<MqttBroker> Servers { get; init; }

    /// <summary>
    /// The unique MQTT Client ID used for the broker connection.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// The Sparkplug B Group ID.
    /// </summary>
    public required string GroupId { get; init; }

    /// <summary>
    /// The Sparkplug B Edge Node ID.
    /// </summary>
    public required string EdgeNodeId { get; init; }

    /// <summary>
    /// The optional ID of the Primary Host Application to monitor.
    /// If set, the client will apply Birth-Gating.
    /// </summary>
    public string? PrimaryHostId { get; init; }

    /// <summary>
    /// The maximum duration to wait for the Primary Host Application to become ONLINE
    /// before initiating a failover to the next configured MQTT server.
    /// </summary>
    public TimeSpan PrimaryHostWaitTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Configuration for Store-and-Forward behavior.
    /// </summary>
    public required StoreAndForwardOptions StoreAndForward { get; init; }

    /// <summary>
    /// The optional username for MQTT authentication.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// The optional password for MQTT authentication.
    /// </summary>
    public string? Password { get; init; }
}