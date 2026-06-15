using Microsoft.Extensions.Logging;
using PlugB.Abstractions;
using PlugB.Events;
using PlugB.Internal.Mapping;
using PlugB.Internal.State;
using PlugB.Internal.Transport;
using PlugB.Models;
using PlugB.Options;

namespace PlugB;

/// <summary>
/// Implementation of the PlugB Host Application.
/// </summary>
internal class PlugBHost(PlugBHostOptions options, HostNodeRegistry registry, HostSequenceTracker tracker, ILogger? logger) : IPlugBHost
{
    private readonly HostMqttTransport _transport = new(options, registry, tracker, logger);
    private readonly HostNodeRegistry _registry = registry;

    public event EventHandler<NodeBirthEventArgs>? NodeBirth
    {
        add => _transport.NodeBirth += value;
        remove => _transport.NodeBirth -= value;
    }

    public event EventHandler<NodeDeathEventArgs>? NodeDeath
    {
        add => _transport.NodeDeath += value;
        remove => _transport.NodeDeath -= value;
    }

    public event EventHandler<DeviceBirthEventArgs>? DeviceBirth
    {
        add => _transport.DeviceBirth += value;
        remove => _transport.DeviceBirth -= value;
    }

    public event EventHandler<DeviceDeathEventArgs>? DeviceDeath
    {
        add => _transport.DeviceDeath += value;
        remove => _transport.DeviceDeath -= value;
    }

    public event EventHandler<DataChangedEventArgs>? DataChanged
    {
        add => _transport.DataChanged += value;
        remove => _transport.DataChanged -= value;
    }

    public event EventHandler<RebirthRequestedEventArgs>? RebirthRequested
    {
        add => _transport.RebirthRequested += value;
        remove => _transport.RebirthRequested -= value;
    }

    public event EventHandler<DecodeFailedEventArgs>? DecodeFailed
    {
        add => _transport.DecodeFailed += value;
        remove => _transport.DecodeFailed -= value;
    }

    public event EventHandler<bool>? ConnectionChanged
    {
        add => _transport.ConnectionChanged += value;
        remove => _transport.ConnectionChanged -= value;
    }

    public bool IsConnected => _transport.IsConnected;

    public IReadOnlyCollection<HostNode> Nodes => _registry.GetNodes();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _transport.StartAsync(cancellationToken);
    }

    public Task RequestRebirthAsync(string groupId, string edgeNodeId, CancellationToken cancellationToken = default)
    {
        return _transport.SendRebirthCommandAsync(groupId, edgeNodeId, cancellationToken);
    }

    public Task SendNodeCommandAsync(string groupId, string edgeNodeId, IEnumerable<Metric> metrics, CancellationToken cancellationToken = default)
    {
        var topic = TopicGenerator.GetNodeTopic(groupId, edgeNodeId, TopicGenerator.MsgTypeNodeCommand);
        return _transport.PublishCommandAsync(topic, metrics, cancellationToken);
    }

    public Task SendDeviceCommandAsync(string groupId, string edgeNodeId, string deviceId, IEnumerable<Metric> metrics, CancellationToken cancellationToken = default)
    {
        var topic = TopicGenerator.GetDeviceTopic(groupId, edgeNodeId, deviceId, TopicGenerator.MsgTypeDeviceCommand);
        return _transport.PublishCommandAsync(topic, metrics, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _transport.DisposeAsync();
    }
}