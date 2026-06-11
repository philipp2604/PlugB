using PlugB.Options;

namespace PlugB.Storage;

/// <summary>
/// Represents a single metric buffered in the Store-and-Forward queue.
/// </summary>
public record ForwardEntry(string TargetTopic, SparkplugMessageType MessageType, Metric Metric);