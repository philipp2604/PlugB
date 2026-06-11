using PlugB.Options;

namespace PlugB.Internal.Transport;

/// <summary>
/// Manages the configured MQTT servers and provides rotation capabilities for failover scenarios.
/// </summary>
internal class ServerSelector
{
    private readonly IReadOnlyList<MqttBroker> _servers;
    private int _currentIndex;
    private readonly Lock _lock = new();

    public ServerSelector(IReadOnlyList<MqttBroker> servers)
    {
        if (servers == null || servers.Count == 0)
            throw new ArgumentException("At least one MQTT server must be configured.");

        _servers = servers;
        _currentIndex = 0;
    }

    /// <summary>
    /// Retrieves the currently active server configuration.
    /// </summary>
    public MqttBroker GetCurrent()
    {
        lock (_lock) return _servers[_currentIndex];
    }

    /// <summary>
    /// Moves the selector to the next server in the list. Wraps around to the beginning if the end is reached.
    /// </summary>
    public void MoveNext()
    {
        lock (_lock)
        {
            _currentIndex = (_currentIndex + 1) % _servers.Count;
        }
    }
}