using Microsoft.Extensions.Logging;
using PlugB.Abstractions;
using PlugB.Internal.State;
using PlugB.Options;

namespace PlugB.Builders;

/// <summary>
/// Fluent Builder to configure and create a new PlugB Host Application.
/// </summary>
public class PlugBHostBuilder
{
    private readonly List<MqttBroker> _servers = [];
    private string? _hostId;
    private string _clientId = Guid.NewGuid().ToString();
    private readonly List<string> _groupFilters = [];
    private bool _rebirthOnGap = true;
    private string? _username;
    private string? _password;
    private ILogger? _logger;

    public PlugBHostBuilder WithBroker(string address, int port = 1883)
    {
        _servers.Clear();
        _servers.Add(new MqttBroker(address, port, false));
        return this;
    }

    public PlugBHostBuilder WithBrokers(params MqttBroker[] servers)
    {
        _servers.Clear();
        _servers.AddRange(servers);
        return this;
    }

    public PlugBHostBuilder WithHostId(string hostId)
    {
        _hostId = hostId;
        return this;
    }

    public PlugBHostBuilder WithClientId(string clientId)
    {
        _clientId = clientId;
        return this;
    }

    public PlugBHostBuilder WithGroupFilter(string groupId)
    {
        _groupFilters.Add(groupId);
        return this;
    }

    public PlugBHostBuilder WithRebirthOnGap(bool enabled)
    {
        _rebirthOnGap = enabled;
        return this;
    }

    public PlugBHostBuilder WithCredentials(string username, string password)
    {
        _username = username;
        _password = password;
        return this;
    }

    public PlugBHostBuilder WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    public IPlugBHost Build()
    {
        if (string.IsNullOrWhiteSpace(_hostId))
        {
            throw new InvalidOperationException("HostId is required. Call WithHostId() before building.");
        }

        // Fallback if no server was specified
        if (_servers.Count == 0)
        {
            _servers.Add(new MqttBroker("localhost", 1883, false));
        }

        var options = new PlugBHostOptions
        {
            Servers = _servers,
            HostId = _hostId,
            ClientId = _clientId,
            GroupFilters = _groupFilters,
            RebirthOnGap = _rebirthOnGap,
            Username = _username,
            Password = _password
        };

        var registry = new HostNodeRegistry();
        var tracker = new HostSequenceTracker();

        return new PlugBHost(options, registry, tracker, _logger);
    }
}