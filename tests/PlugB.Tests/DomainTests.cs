using FluentAssertions;
using PlugB.Builders;
using PlugB.Internal.Domain;
using PlugB.Models;

namespace PlugB.Tests;

public class DomainTests
{
    [Fact]
    public void TopicGenerator_Should_Generate_Correct_Topics()
    {
        var nodeTopic = TopicGenerator.GetNodeTopic("FactoryA", "Edge1", TopicGenerator.MsgTypeNodeBirth);
        nodeTopic.Should().Be("spBv1.0/FactoryA/NBIRTH/Edge1");

        var deviceTopic = TopicGenerator.GetDeviceTopic("FactoryA", "Edge1", "PLC1", TopicGenerator.MsgTypeDeviceData);
        deviceTopic.Should().Be("spBv1.0/FactoryA/DDATA/Edge1/PLC1");
    }

    [Fact]
    public void DataTypeConverter_Should_Map_UnsignedInts_To_LongValue()
    {
        // Arrange
        var metric = MetricBuilder.Create("TestUInt")
            .WithValue(42u, PlugBDataType.UInt32)
            .Build();

        var protoMetric = new Com.Cirruslink.Sparkplug.Protobuf.Payload.Types.Metric();

        // Act
        DataTypeConverter.ApplyToProtoMetric(protoMetric, metric);

        // Assert
        protoMetric.Datatype.Should().Be((uint)PlugBDataType.UInt32);
        // unsigned ints must be in LongValue (uint64 field in protobuf)
        protoMetric.ValueCase.Should().Be(Com.Cirruslink.Sparkplug.Protobuf.Payload.Types.Metric.ValueOneofCase.LongValue);
        protoMetric.LongValue.Should().Be(42ul);
    }

    [Fact]
    public void PayloadBuilder_Should_Set_Timestamps_To_Utc_Epoch()
    {
        // Arrange
        long expectedTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var metric = MetricBuilder.Create("Temp").WithValue(22.5).WithTimestamp(expectedTs).Build();

        // Act
        var payload = PayloadBuilder.Build([metric], seq: 5, timestampMs: (ulong)expectedTs);

        // Assert
        payload.Timestamp.Should().Be((ulong)expectedTs);
        payload.Seq.Should().Be(5ul);
        payload.Metric.First().Timestamp.Should().Be((ulong)expectedTs);
    }

    [Fact]
    public void MetricBuilder_Should_Include_Alias()
    {
        // Arrange & Act
        var metric = MetricBuilder.Create("Speed")
            .WithAlias(100)
            .WithValue(1500)
            .Build();

        // Assert
        metric.Alias.Should().Be(100ul);
    }
}