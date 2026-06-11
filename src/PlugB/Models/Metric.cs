namespace PlugB.Models;

/// <summary>
/// Represents a fully configured Sparkplug-B metric.
/// </summary>
public record Metric
{
    public required string Name { get; init; }
    public ulong? Alias { get; init; }
    public PlugBDataType DataType { get; init; }

    /// <summary>
    /// The value of the metric.
    /// </summary>
    public required object Value { get; init; }

    public long TimestampMilliseconds { get; init; }

    /// <summary>
    /// If true, the metric represents historical data (e.g., queued while offline).
    /// </summary>
    public bool IsHistorical { get; init; }

    /// <summary>
    /// Optional properties attached to this metric (e.g., engineering units, limits).
    /// </summary>
    public PlugBPropertySet? Properties { get; init; }
}