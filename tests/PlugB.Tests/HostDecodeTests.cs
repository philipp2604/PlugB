// tests/PlugB.Tests/HostDecodeTests.cs

using FluentAssertions;
using PlugB.Builders;
using PlugB.Internal.Mapping;
using PlugB.Models;
using ProtoPayload = Com.Cirruslink.Sparkplug.Protobuf.Payload;
using ProtoMetric = Com.Cirruslink.Sparkplug.Protobuf.Payload.Types.Metric;

namespace PlugB.Tests;

public class HostDecodeTests
{
    [Fact]
    public void PayloadDecoder_Should_RoundTrip_Basic_DataTypes()
    {
        // Arrange
        var metrics = new[]
        {
            MetricBuilder.Create("Int8").WithValue((sbyte)10, PlugBDataType.Int8).Build(),
            MetricBuilder.Create("Int16").WithValue((short)20, PlugBDataType.Int16).Build(),
            MetricBuilder.Create("Int32").WithValue((int)30, PlugBDataType.Int32).Build(),
            MetricBuilder.Create("Int64").WithValue((long)-40, PlugBDataType.Int64).Build(),
            MetricBuilder.Create("UInt8").WithValue((byte)50, PlugBDataType.UInt8).Build(),
            MetricBuilder.Create("UInt16").WithValue((ushort)60, PlugBDataType.UInt16).Build(),
            MetricBuilder.Create("UInt32").WithValue((uint)70, PlugBDataType.UInt32).Build(),
            MetricBuilder.Create("UInt64").WithValue((ulong)80, PlugBDataType.UInt64).Build(),
            MetricBuilder.Create("Float").WithValue(90.5f, PlugBDataType.Float).Build(),
            MetricBuilder.Create("Double").WithValue(100.5, PlugBDataType.Double).Build(),
            MetricBuilder.Create("Boolean").WithValue(true, PlugBDataType.Boolean).Build(),
            MetricBuilder.Create("String").WithValue("Test", PlugBDataType.String).Build(),
            MetricBuilder.Create("DateTime").WithValue(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc), PlugBDataType.DateTime).Build(),
            MetricBuilder.Create("Bytes").WithValue(new byte[] { 1, 2, 3 }, PlugBDataType.Bytes).Build()
        };

        var payload = PayloadBuilder.Build(metrics, 1, 1000);

        // Act
        var decodedMetrics = PayloadDecoder.ToMetrics(payload);

        // Assert
        decodedMetrics.Should().HaveCount(14);
        decodedMetrics.Single(m => m.Name == "Int8").Value.Should().Be((sbyte)10);
        decodedMetrics.Single(m => m.Name == "Int16").Value.Should().Be((short)20);
        decodedMetrics.Single(m => m.Name == "Int32").Value.Should().Be(30);
        decodedMetrics.Single(m => m.Name == "Int64").Value.Should().Be(-40L);
        decodedMetrics.Single(m => m.Name == "UInt8").Value.Should().Be((byte)50);
        decodedMetrics.Single(m => m.Name == "UInt16").Value.Should().Be((ushort)60);
        decodedMetrics.Single(m => m.Name == "UInt32").Value.Should().Be(70u);
        decodedMetrics.Single(m => m.Name == "UInt64").Value.Should().Be(80ul);
        decodedMetrics.Single(m => m.Name == "Float").Value.Should().Be(90.5f);
        decodedMetrics.Single(m => m.Name == "Double").Value.Should().Be(100.5);
        decodedMetrics.Single(m => m.Name == "Boolean").Value.Should().Be(true);
        decodedMetrics.Single(m => m.Name == "String").Value.Should().Be("Test");
        decodedMetrics.Single(m => m.Name == "DateTime").Value.Should().Be(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        decodedMetrics.Single(m => m.Name == "Bytes").Value.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void PayloadDecoder_Should_Decode_Negative_SmallInts_From_Raw_Protobuf()
    {
        // Arrange
        var protoMetric = new ProtoMetric
        {
            Name = "NegInt8",
            Datatype = (uint)PlugBDataType.Int8,
            IntValue = unchecked((uint)(sbyte)-15)
        };
        var payload = new ProtoPayload();
        payload.Metric.Add(protoMetric);

        // Act
        var decodedMetrics = PayloadDecoder.ToMetrics(payload);

        // Assert
        decodedMetrics.Should().HaveCount(1);
        decodedMetrics[0].Value.Should().Be((sbyte)-15);
    }

    [Fact]
    public void PayloadDecoder_Should_Decode_Complex_DataTypes()
    {
        // Arrange
        var propSet = new PlugBPropertySet();
        propSet.AddProperty("Prop1", PlugBDataType.String, "PropVal");

        var dataSet = new PlugBDataSet
        {
            Columns = { "Col1" },
            Types = { PlugBDataType.Int32 },
            Rows = { new PlugBDataSetRow { Elements = { 42 } } }
        };

        var template = new PlugBTemplate
        {
            IsDefinition = true,
            Metrics = { MetricBuilder.Create("TplMetric").WithValue(1.5, PlugBDataType.Double).Build() }
        };

        var metrics = new[]
        {
            MetricBuilder.Create("PropertySetMetric").WithValue(propSet, PlugBDataType.PropertySet).Build(),
            MetricBuilder.Create("DataSetMetric").WithValue(dataSet, PlugBDataType.DataSet).Build(),
            MetricBuilder.Create("TemplateMetric").WithValue(template, PlugBDataType.Template).Build()
        };

        var payload = PayloadBuilder.Build(metrics, 1, 1000);

        // Act
        var decoded = PayloadDecoder.ToMetrics(payload);

        // Assert
        var decodedPropSet = decoded.Single(m => m.Name == "PropertySetMetric").Value as PlugBPropertySet;
        decodedPropSet.Should().NotBeNull();
        decodedPropSet!.Properties["Prop1"].Value.Should().Be("PropVal");

        var decodedDataSet = decoded.Single(m => m.Name == "DataSetMetric").Value as PlugBDataSet;
        decodedDataSet.Should().NotBeNull();
        decodedDataSet!.Columns.Should().Contain("Col1");
        decodedDataSet.Rows[0].Elements[0].Should().Be(42);

        var decodedTemplate = decoded.Single(m => m.Name == "TemplateMetric").Value as PlugBTemplate;
        decodedTemplate.Should().NotBeNull();
        decodedTemplate!.IsDefinition.Should().BeTrue();
        decodedTemplate.Metrics[0].Name.Should().Be("TplMetric");
        decodedTemplate.Metrics[0].Value.Should().Be(1.5);
    }

    [Fact]
    public void PayloadDecoder_Should_Decode_Null_And_Alias_Properly()
    {
        // Arrange
        var protoMetric = new ProtoMetric
        {
            Alias = 123,
            Datatype = (uint)PlugBDataType.String,
            IsNull = true
        };
        var payload = new ProtoPayload();
        payload.Metric.Add(protoMetric);

        // Act
        var decodedMetrics = PayloadDecoder.ToMetrics(payload);

        // Assert
        var m = decodedMetrics[0];
        m.Alias.Should().Be(123ul);
        m.Value.Should().BeNull();
        m.Name.Should().BeEmpty();
    }
}