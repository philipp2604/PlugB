namespace PlugB.Options;

/// <summary>
/// Contains information about a Store-and-Forward buffer overflow event.
/// </summary>
/// <param name="DroppedCount">The number of metrics dropped due to eviction.</param>
public record BufferOverflowInfo(int DroppedCount);