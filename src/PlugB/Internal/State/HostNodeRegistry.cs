using PlugB.Models;

namespace PlugB.Internal.State;

internal enum DataProcessingResult
{
    Success,
    UnknownAlias,
    NodeNotFound,
    DeviceNotFound
}

/// <summary>
/// Maintains the in-memory state representation of all observed Sparkplug B Edge Nodes and Devices.
/// Handles alias resolution and applies data updates.
/// </summary>
internal class HostNodeRegistry
{
    private class InternalDevice
    {
        public string DeviceId { get; set; } = string.Empty;
        public bool Online { get; set; }
        public Dictionary<string, Metric> Metrics { get; set; } = [];
        public Dictionary<ulong, string> AliasToName { get; set; } = [];

        public HostDevice ToSnapshot() => new HostDevice
        {
            DeviceId = DeviceId,
            Online = Online,
            Metrics = new Dictionary<string, Metric>(Metrics)
        };
    }

    private class InternalNode
    {
        public string GroupId { get; set; } = string.Empty;
        public string EdgeNodeId { get; set; } = string.Empty;
        public bool Online { get; set; }
        public ulong BdSeq { get; set; }
        public Dictionary<string, Metric> Metrics { get; set; } = [];
        public Dictionary<string, InternalDevice> Devices { get; set; } = [];
        public Dictionary<ulong, string> AliasToName { get; set; } = [];

        public HostNode ToSnapshot() => new HostNode
        {
            GroupId = GroupId,
            EdgeNodeId = EdgeNodeId,
            Online = Online,
            BdSeq = BdSeq,
            Metrics = new Dictionary<string, Metric>(Metrics),
            Devices = Devices.ToDictionary(k => k.Key, v => v.Value.ToSnapshot())
        };
    }

    private readonly Dictionary<string, InternalNode> _nodes = [];
    private readonly Lock _lock = new();

    private static string GetNodeKey(string groupId, string edgeNodeId) => $"{groupId}/{edgeNodeId}";

    public HostNode ProcessNBirth(string groupId, string edgeNodeId, ulong bdSeq, IEnumerable<Metric> metrics)
    {
        lock (_lock)
        {
            var key = GetNodeKey(groupId, edgeNodeId);

            var node = new InternalNode
            {
                GroupId = groupId,
                EdgeNodeId = edgeNodeId,
                Online = true,
                BdSeq = bdSeq
            };

            ProcessMetricsIntoMap(node.Metrics, node.AliasToName, metrics);
            _nodes[key] = node;

            return node.ToSnapshot();
        }
    }

    public HostDevice? ProcessDBirth(string groupId, string edgeNodeId, string deviceId, IEnumerable<Metric> metrics)
    {
        lock (_lock)
        {
            var key = GetNodeKey(groupId, edgeNodeId);
            if (!_nodes.TryGetValue(key, out var node)) return null;

            var device = new InternalDevice
            {
                DeviceId = deviceId,
                Online = true
            };

            ProcessMetricsIntoMap(device.Metrics, device.AliasToName, metrics);
            node.Devices[deviceId] = device;

            return device.ToSnapshot();
        }
    }

    public (DataProcessingResult Result, HostNode? Snapshot) ProcessNData(string groupId, string edgeNodeId, IEnumerable<Metric> metrics)
    {
        lock (_lock)
        {
            var key = GetNodeKey(groupId, edgeNodeId);
            if (!_nodes.TryGetValue(key, out var node)) return (DataProcessingResult.NodeNotFound, null);

            if (!ApplyDataMetrics(node.Metrics, node.AliasToName, metrics))
            {
                return (DataProcessingResult.UnknownAlias, null);
            }

            return (DataProcessingResult.Success, node.ToSnapshot());
        }
    }

    public (DataProcessingResult Result, HostDevice? Snapshot) ProcessDData(string groupId, string edgeNodeId, string deviceId, IEnumerable<Metric> metrics)
    {
        lock (_lock)
        {
            var key = GetNodeKey(groupId, edgeNodeId);
            if (!_nodes.TryGetValue(key, out var node)) return (DataProcessingResult.NodeNotFound, null);
            if (!node.Devices.TryGetValue(deviceId, out var device)) return (DataProcessingResult.DeviceNotFound, null);

            if (!ApplyDataMetrics(device.Metrics, device.AliasToName, metrics))
            {
                return (DataProcessingResult.UnknownAlias, null);
            }

            return (DataProcessingResult.Success, device.ToSnapshot());
        }
    }

    public HostNode? ProcessNDeath(string groupId, string edgeNodeId)
    {
        lock (_lock)
        {
            var key = GetNodeKey(groupId, edgeNodeId);
            if (!_nodes.TryGetValue(key, out var node)) return null;

            node.Online = false;
            foreach (var dev in node.Devices.Values)
            {
                dev.Online = false;
            }
            return node.ToSnapshot();
        }
    }

    public HostDevice? ProcessDDeath(string groupId, string edgeNodeId, string deviceId)
    {
        lock (_lock)
        {
            var key = GetNodeKey(groupId, edgeNodeId);
            if (!_nodes.TryGetValue(key, out var node)) return null;
            if (!node.Devices.TryGetValue(deviceId, out var device)) return null;

            device.Online = false;
            return device.ToSnapshot();
        }
    }

    public IReadOnlyCollection<HostNode> GetNodes()
    {
        lock (_lock)
        {
            return _nodes.Values.Select(n => n.ToSnapshot()).ToList();
        }
    }

    private static void ProcessMetricsIntoMap(Dictionary<string, Metric> metricsMap, Dictionary<ulong, string> aliasMap, IEnumerable<Metric> incoming)
    {
        foreach (var m in incoming)
        {
            if (string.IsNullOrEmpty(m.Name)) continue;

            metricsMap[m.Name] = m;
            if (m.Alias.HasValue)
            {
                aliasMap[m.Alias.Value] = m.Name;
            }
        }
    }

    private static bool ApplyDataMetrics(Dictionary<string, Metric> metricsMap, Dictionary<ulong, string> aliasMap, IEnumerable<Metric> incoming)
    {
        foreach (var m in incoming)
        {
            string metricName = m.Name;

            if (string.IsNullOrEmpty(metricName))
            {
                if (!m.Alias.HasValue || !aliasMap.TryGetValue(m.Alias.Value, out var resolvedName))
                {
                    return false; // metric has no Name and missing/unknown Alias
                }
                metricName = resolvedName;
            }
            else
            {
                // if DATA provides both Name and Alias, update the alias registry
                if (m.Alias.HasValue)
                {
                    aliasMap[m.Alias.Value] = metricName;
                }
            }

            // ensure the stored metric has a fully resolved name
            var updatedMetric = m with { Name = metricName };
            metricsMap[metricName] = updatedMetric;
        }

        return true;
    }
}