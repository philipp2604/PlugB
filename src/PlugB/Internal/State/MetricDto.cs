using PlugB.Options;
using System.Text.Json;

/// <summary>
/// Data Transfer Object for serializing a Sparkplug <see cref="Metric"/>.
/// </summary>
internal class MetricDto
{
    /// <summary>
    /// Gets or sets the metric name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional metric alias.
    /// </summary>
    public ulong? Alias { get; set; }

    /// <summary>
    /// Gets or sets the Sparkplug data type.
    /// </summary>
    public PlugBDataType DataType { get; set; }

    /// <summary>
    /// Gets or sets the original timestamp of the data point.
    /// </summary>
    public long TimestampMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this metric is historical.
    /// </summary>
    public bool IsHistorical { get; set; }

    /// <summary>
    /// Gets or sets the raw JSON representation of the metric's value.
    /// </summary>
    public JsonElement ValueElement { get; set; }
}