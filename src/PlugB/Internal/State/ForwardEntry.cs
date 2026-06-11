using PlugB.Internal.Transport;
using PlugB.Options;

namespace PlugB.Internal.State;

/// <summary>
/// Represents a single metric buffered in the Store-and-Forward queue.
/// </summary>
internal record ForwardEntry(string TargetTopic, SparkplugMessageType MessageType, Metric Metric);