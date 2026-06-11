namespace PlugB.Options;

/// <summary>
/// Represents a configured MQTT Broker endpoint for the Edge Node.
/// </summary>
/// <param name="Address">The hostname or IP address of the MQTT broker.</param>
/// <param name="Port">The port number of the MQTT broker (default is 1883).</param>
/// <param name="UseTls">Indicates whether to use a secure TLS/SSL connection.</param>
public record MqttBroker(string Address, int Port = 1883, bool UseTls = false);