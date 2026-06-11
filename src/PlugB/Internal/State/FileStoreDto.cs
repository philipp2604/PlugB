using System.Text.Json;
using PlugB.Options;
using PlugB.Storage;

namespace PlugB.Internal.State;

/// <summary>
/// Data Transfer Object for serializing a <see cref="ForwardEntry"/> to a JSON line.
/// </summary>
internal class ForwardEntryDto
{
    /// <summary>
    /// Gets or sets the target MQTT topic.
    /// </summary>
    public string TargetTopic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the internal Sparkplug message type.
    /// </summary>
    public SparkplugMessageType MessageType { get; set; }

    /// <summary>
    /// Gets or sets the metric data associated with this entry.
    /// </summary>
    public MetricDto Metric { get; set; } = new();

    /// <summary>
    /// Converts this DTO back into a valid domain <see cref="ForwardEntry"/>.
    /// </summary>
    /// <returns>The reconstructed domain entry.</returns>
    public ForwardEntry ToForwardEntry()
    {
        object? parsedValue = ParseValue(Metric.ValueElement, Metric.DataType);

        var realMetric = new Metric
        {
            Name = Metric.Name,
            Alias = Metric.Alias,
            DataType = Metric.DataType,
            TimestampMilliseconds = Metric.TimestampMilliseconds,
            IsHistorical = Metric.IsHistorical,
            Value = parsedValue ?? new object() // Fallback
        };

        return new ForwardEntry(TargetTopic, MessageType, realMetric);
    }

    /// <summary>
    /// Creates a new DTO from an existing domain <see cref="ForwardEntry"/>.
    /// </summary>
    /// <param name="entry">The domain entry to serialize.</param>
    /// <returns>A populated DTO ready for JSON serialization.</returns>
    public static ForwardEntryDto FromForwardEntry(ForwardEntry entry)
    {
        return new ForwardEntryDto
        {
            TargetTopic = entry.TargetTopic,
            MessageType = entry.MessageType,
            Metric = new MetricDto
            {
                Name = entry.Metric.Name,
                Alias = entry.Metric.Alias,
                DataType = entry.Metric.DataType,
                TimestampMilliseconds = entry.Metric.TimestampMilliseconds,
                IsHistorical = entry.Metric.IsHistorical,
                ValueElement = JsonSerializer.SerializeToElement(entry.Metric.Value)
            }
        };
    }

    /// <summary>
    /// Parses a raw JsonElement back into the correct underlying C# primitive type.
    /// </summary>
    private static object? ParseValue(JsonElement element, PlugBDataType dataType)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;

        return dataType switch
        {
            PlugBDataType.Int8 => element.GetSByte(),
            PlugBDataType.Int16 => element.GetInt16(),
            PlugBDataType.Int32 => element.GetInt32(),
            PlugBDataType.Int64 => element.GetInt64(),
            PlugBDataType.UInt8 => element.GetByte(),
            PlugBDataType.UInt16 => element.GetUInt16(),
            PlugBDataType.UInt32 => element.GetUInt32(),
            PlugBDataType.UInt64 => element.GetUInt64(),
            PlugBDataType.Float => element.GetSingle(),
            PlugBDataType.Double => element.GetDouble(),
            PlugBDataType.Boolean => element.GetBoolean(),
            PlugBDataType.String or PlugBDataType.Text or PlugBDataType.Uuid => element.GetString(),
            PlugBDataType.DateTime => DateTimeOffset.FromUnixTimeMilliseconds(element.GetInt64()).UtcDateTime,
            _ => element.GetRawText() // complex types stored as raw json
        };
    }
}