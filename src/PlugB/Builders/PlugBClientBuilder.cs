using Microsoft.Extensions.Logging;
using PlugB.Abstractions;
using PlugB.Internal.State;
using PlugB.Options;

namespace PlugB.Builders;

/// <summary>
/// Fluent Builder to configure and create a new PlugB Edge Node Client.
/// </summary>
public class PlugBClientBuilder
{
    private readonly List<MqttBroker> _servers = [];
    private string _clientId = Guid.NewGuid().ToString();
    private string _groupId = "SparkplugB";
    private string _edgeNodeId = "EdgeNode1";
    private string? _primaryHostId;
    private TimeSpan _primaryHostWaitTimeout = TimeSpan.FromSeconds(30);
    private string? _username;
    private string? _password;
    private ILogger? _logger;
    private readonly List<Metric> _nodeMetrics = [];
    private readonly StoreAndForwardOptions _sfOptions = new();

    /// <summary>
    /// Configures a single MQTT broker endpoint. Replaces any previously configured servers.
    /// </summary>
    /// <param name="address">The hostname or IP address.</param>
    /// <param name="port">The port number (default 1883).</param>
    public PlugBClientBuilder WithBroker(string address, int port = 1883)
    {
        _servers.Clear();
        _servers.Add(new MqttBroker(address, port, false));
        return this;
    }

    /// <summary>
    /// Configures multiple MQTT broker endpoints for failover support.
    /// The client will iterate through these servers based on priority (index order).
    /// </summary>
    /// <param name="servers">An array of configured MQTT servers.</param>
    public PlugBClientBuilder WithBrokers(params MqttBroker[] servers)
    {
        _servers.Clear();
        _servers.AddRange(servers);
        return this;
    }

    /// <summary>
    /// Sets the Sparkplug B identification parameters.
    /// </summary>
    /// <param name="groupId">The Group ID.</param>
    /// <param name="edgeNodeId">The Edge Node ID.</param>
    public PlugBClientBuilder WithNodeId(string groupId, string edgeNodeId)
    {
        _groupId = groupId;
        _edgeNodeId = edgeNodeId;
        return this;
    }

    /// <summary>
    /// Sets a specific MQTT Client ID. If not called, a random GUID is used.
    /// </summary>
    /// <param name="clientId">The underlying MQTT Client ID.</param>
    public PlugBClientBuilder WithClientId(string clientId)
    {
        _clientId = clientId;
        return this;
    }

    /// <summary>
    /// Enables Primary Host Awareness. The Edge Node will wait for this host to publish 
    /// an ONLINE state before sending its own birth certificates.
    /// </summary>
    /// <param name="primaryHostId">The ID of the Primary Host Application.</param>
    /// <param name="waitTimeout">Optional timeout before triggering failover (default is 30s).</param>
    public PlugBClientBuilder WithPrimaryHost(string primaryHostId, TimeSpan? waitTimeout = null)
    {
        _primaryHostId = primaryHostId;
        if (waitTimeout.HasValue) _primaryHostWaitTimeout = waitTimeout.Value;

        _sfOptions.Enabled = true;
        return this;
    }

    /// <summary>
    /// Sets the authentication credentials for the MQTT connection.
    /// </summary>
    /// <param name="username">The MQTT username.</param>
    /// <param name="password">The MQTT password.</param>
    public PlugBClientBuilder WithCredentials(string username, string password)
    {
        _username = username;
        _password = password;
        return this;
    }

    /// <summary>
    /// Injects an optional logger implementation.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public PlugBClientBuilder WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Adds a metric directly to the Edge Node. This metric will be published in the NBIRTH message.
    /// </summary>
    /// <param name="name">The name of the metric.</param>
    /// <param name="dataType">The Sparkplug B data type.</param>
    /// <param name="value">The actual value matching the data type.</param>
    public PlugBClientBuilder WithNodeMetric(string name, PlugBDataType dataType, object value)
    {
        var metric = MetricBuilder.Create(name).WithValue(value, dataType).Build();
        _nodeMetrics.Add(metric);
        return this;
    }

    /// <summary>
    /// Configures Store-and-Forward caching behavior for disconnected states.
    /// </summary>
    public PlugBClientBuilder WithStoreAndForward(Action<StoreAndForwardOptions> configure)
    {
        _sfOptions.Enabled = true;
        configure(_sfOptions);
        return this;
    }

    /// <summary>
    /// Validates the configuration and builds the PlugB Client.
    /// </summary>
    /// <returns>An initialized Edge Node client ready to be started.</returns>
    public IPlugBClient Build()
    {
        // Fallback if no server was specified
        if (_servers.Count == 0)
        {
            _servers.Add(new MqttBroker("localhost", 1883, false));
        }

        if (_sfOptions.Enabled && _sfOptions.Store == null)
        {
            _sfOptions.Store = new InMemoryForwardStore(_sfOptions.Capacity, _sfOptions.Eviction);
        }

        var options = new PlugBOptions
        {
            Servers = _servers,
            ClientId = _clientId,
            GroupId = _groupId,
            EdgeNodeId = _edgeNodeId,
            PrimaryHostId = _primaryHostId,
            PrimaryHostWaitTimeout = _primaryHostWaitTimeout,
            Username = _username,
            Password = _password,
            StoreAndForward = _sfOptions
        };

        return new PlugBClient(options, _nodeMetrics, _logger);
    }
}