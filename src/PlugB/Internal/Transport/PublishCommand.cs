using PlugB.Models;
using PlugB.Storage;

namespace PlugB.Internal.Transport;

internal record PublishCommand(string Topic, SparkplugMessageType Type, IEnumerable<Metric> Metrics);