namespace PlugB.Options;

/// <summary>
/// Represents a Sparkplug B DataSet as table
/// </summary>
public record PlugBDataSet
{
    public List<string> Columns { get; init; } = [];
    public List<PlugBDataType> Types { get; init; } = [];
    public List<PlugBDataSetRow> Rows { get; init; } = [];
}

public record PlugBDataSetRow
{
    /// <summary>
    /// The values must match the Types defined in the parent PlugBDataSet.
    /// </summary>
    public List<object?> Elements { get; init; } = [];
}