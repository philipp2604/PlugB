using Com.Cirruslink.Sparkplug.Protobuf;
using PlugB.Options;

namespace PlugB.Internal.Domain;

internal static class PayloadBuilder
{
    /// <summary>
    /// Builds the Protobuf Payload. 
    /// Note: NDEATH payloads have no sequence number (seq = null).
    /// </summary>
    public static Payload Build(IEnumerable<Metric> metrics, ulong? seq, ulong timestampMs)
    {
        var payload = new Payload
        {
            Timestamp = timestampMs
        };

        if (seq.HasValue)
        {
            payload.Seq = seq.Value;
        }

        foreach (var metric in metrics)
        {
            var protoMetric = new Payload.Types.Metric
            {
                Name = metric.Name,
                Timestamp = (ulong)metric.TimestampMilliseconds,
                IsHistorical = metric.IsHistorical
            };

            if (metric.Alias.HasValue)
            {
                protoMetric.Alias = metric.Alias.Value;
            }

            DataTypeConverter.ApplyToProtoMetric(protoMetric, metric);
            payload.Metric.Add(protoMetric);
        }

        return payload;
    }
}