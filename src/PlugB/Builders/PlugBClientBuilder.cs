using Microsoft.Extensions.Logging;
using PlugB.Abstractions;
using PlugB.Options;

namespace PlugB.Builders;

/// <summary>
/// Fluent Builder to configure and create a new PlugB Edge Node Client.
/// </summary>
public class PlugBClientBuilder
{
    private string _brokerAddress = "localhost";
    private int _brokerPort = 1883;
    private string _clientId = Guid.NewGuid().ToString();
    private string _groupId = "SparkplugB";
    private string _edgeNodeId = "EdgeNode1";
    private string? _primaryHostId;
    private string? _username;
    private string? _password;
    private bool _useTls;
    private ILogger? _logger;
    private readonly List<Metric> _nodeMetrics = [];

    public PlugBClientBuilder WithBroker(string address, int port = 1883)
    {
        _brokerAddress = address;
        _brokerPort = port;
        return this;
    }

    public PlugBClientBuilder WithNodeId(string groupId, string edgeNodeId)
    {
        _groupId = groupId;
        _edgeNodeId = edgeNodeId;
        return this;
    }

    public PlugBClientBuilder WithClientId(string clientId)
    {
        _clientId = clientId;
        return this;
    }

    public PlugBClientBuilder WithPrimaryHost(string primaryHostId)
    {
        _primaryHostId = primaryHostId;
        return this;
    }

    public PlugBClientBuilder WithCredentials(string username, string password)
    {
        _username = username;
        _password = password;
        return this;
    }

    public PlugBClientBuilder UseTls(bool useTls = true)
    {
        _useTls = useTls;
        return this;
    }

    public PlugBClientBuilder WithLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Adds a metric directly to the Edge Node. This metric will be published in the NBIRTH message.
    /// </summary>
    public PlugBClientBuilder WithNodeMetric(string name, PlugBDataType dataType, object value)
    {
        var metric = MetricBuilder.Create(name).WithValue(value, dataType).Build();
        _nodeMetrics.Add(metric);
        return this;
    }

    /// <summary>
    /// Validates the configuration and builds the PlugB Client.
    /// </summary>
    public IPlugBClient Build()
    {
        var options = new PlugBOptions
        {
            BrokerAddress = _brokerAddress,
            BrokerPort = _brokerPort,
            ClientId = _clientId,
            GroupId = _groupId,
            EdgeNodeId = _edgeNodeId,
            PrimaryHostId = _primaryHostId,
            Username = _username,
            Password = _password,
            UseTls = _useTls
        };

        return new PlugBClient(options, _nodeMetrics, _logger);
    }
}