namespace PlugB.Models;

/// <summary>
/// Represents a snapshot of a Sparkplug B edge node from the perspective of a Host Application.
/// </summary>
public record HostNode
{
    public required string GroupId { get; init; }
    public required string EdgeNodeId { get; init; }
    public bool Online { get; init; }
    public ulong BdSeq { get; init; }
    public IReadOnlyDictionary<string, Metric> Metrics { get; init; } = new Dictionary<string, Metric>();
    public IReadOnlyDictionary<string, HostDevice> Devices { get; init; } = new Dictionary<string, HostDevice>();
}