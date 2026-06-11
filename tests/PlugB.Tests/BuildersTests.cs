using FluentAssertions;
using PlugB.Builders;
using PlugB.Internal.Domain;
using PlugB.Models;

namespace PlugB.Tests;

public class BuildersTests
{
    [Fact]
    public void MetricBuilder_Should_Set_IsHistorical_And_PropertySet()
    {
        // Arrange & Act
        var metric = MetricBuilder.Create("HistoricalTemp")
            .WithValue(22.5, PlugBDataType.Double)
            .IsHistorical(true)
            .WithProperty("EngUnit", PlugBDataType.String, "Celsius")
            .Build();

        // Assert Model
        metric.IsHistorical.Should().BeTrue();
        metric.Properties.Should().NotBeNull();
        metric.Properties!.Properties.Should().ContainKey("EngUnit");

        // Assert Protobuf Mapping
        var payload = PayloadBuilder.Build([metric], 1, 123456);
        var protoMetric = payload.Metric[0];

        protoMetric.IsHistorical.Should().BeTrue("the is_historical flag must be serialized");
        protoMetric.Properties.Should().NotBeNull();

        var engUnitIndex = protoMetric.Properties.Keys.IndexOf("EngUnit");
        engUnitIndex.Should().BeGreaterThanOrEqualTo(0);
        protoMetric.Properties.Values[engUnitIndex].StringValue.Should().Be("Celsius");
    }
}