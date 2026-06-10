namespace PlugB.Options;

/// <summary>
/// Represents a fully configured Sparkplug-B metric.
/// </summary>
public record Metric
{
    public required string Name { get; init; }
    public ulong? Alias { get; init; }
    public PlugBDataType DataType { get; init; }

    /// <summary>
    /// The value of the metric. Expected C# types:
    /// - Primitives (int, long, float, double, bool, string)
    /// - byte[] for Bytes
    /// - PlugBDataSet for DataSet
    /// - PlugBTemplate for Template
    /// </summary>
    public required object Value { get; init; }

    public long TimestampMilliseconds { get; init; }

    /// <summary>
    /// If true, the metric represents historical data (e.g., queued while offline).
    /// </summary>
    public bool IsHistorical { get; init; }
}