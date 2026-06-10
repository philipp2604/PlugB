using System;
using System.Collections.Generic;
using System.Text;

namespace PlugB.Options;

/// <summary>
/// Represents a Sparkplug B Template (User Defined Type / UDT).
/// </summary>
public record PlugBTemplate
{
    public string? Version { get; init; }
    public string? TemplateRef { get; init; }
    public bool IsDefinition { get; init; }
    public List<Metric> Metrics { get; init; } = [];
    public List<PlugBTemplateParameter> Parameters { get; init; } = [];
}

public record PlugBTemplateParameter
{
    public required string Name { get; init; }
    public PlugBDataType DataType { get; init; }
    public required object Value { get; init; }
}
