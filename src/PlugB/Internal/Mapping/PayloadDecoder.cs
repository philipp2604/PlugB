using Com.Cirruslink.Sparkplug.Protobuf;
using PlugB.Models;

namespace PlugB.Internal.Mapping;

internal static class PayloadDecoder
{
    /// <summary>
    /// Deserializes the raw payload bytes into the Protobuf Payload object.
    /// </summary>
    public static Payload Parse(byte[] payloadBytes)
    {
        return Payload.Parser.ParseFrom(payloadBytes);
    }

    /// <summary>
    /// Converts the Protobuf Payload object into a list of domain-specific metrics.
    /// </summary>
    public static IReadOnlyList<Metric> ToMetrics(Payload payload)
    {
        var metrics = new List<Metric>(payload.Metric.Count);
        foreach (var protoMetric in payload.Metric)
        {
            metrics.Add(DataTypeConverter.FromProtoMetric(protoMetric));
        }
        return metrics;
    }
}