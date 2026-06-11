namespace PlugB.Models;

/// <summary>
/// Represents the current known state of the Primary Host Application.
/// </summary>
public record PrimaryHostState(bool Online, long LastTimestampMs, bool Known);