namespace PlugB.Models;

/// <summary>
/// Represents a snapshot of a Sparkplug B device from the perspective of a Host Application.
/// </summary>
public record HostDevice
{
    public required string DeviceId { get; init; }
    public bool Online { get; init; }
    public IReadOnlyDictionary<string, Metric> Metrics { get; init; } = new Dictionary<string, Metric>();
}