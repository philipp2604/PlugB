namespace PlugB.Models;

/// <summary>
/// Represents a set of properties attached to a Sparkplug-B Metric.
/// </summary>
public record PlugBPropertySet
{
    public Dictionary<string, PlugBPropertyValue> Properties { get; init; } = [];

    public void AddProperty(string key, PlugBDataType dataType, object? value)
    {
        Properties[key] = new PlugBPropertyValue(dataType, value);
    }
}

/// <summary>
/// Represents a single property value within a PropertySet.
/// </summary>
public record PlugBPropertyValue(PlugBDataType DataType, object? Value);