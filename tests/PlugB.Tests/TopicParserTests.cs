using FluentAssertions;
using PlugB.Internal.Mapping;

namespace PlugB.Tests;

public class TopicParserTests
{
    [Fact]
    public void Parse_Should_Correctly_Identify_Node_Topics()
    {
        var topic = "spBv1.0/FactoryA/NBIRTH/Edge1";

        var result = TopicParser.Parse(topic);

        result.Should().NotBeNull();
        result!.Kind.Should().Be(SparkplugMessageKind.NBirth);
        result.GroupId.Should().Be("FactoryA");
        result.EdgeNodeId.Should().Be("Edge1");
        result.DeviceId.Should().BeNull();
        result.HostId.Should().BeNull();
    }

    [Fact]
    public void Parse_Should_Correctly_Identify_Device_Topics()
    {
        var topic = "spBv1.0/FactoryA/DDATA/Edge1/PLC1";

        var result = TopicParser.Parse(topic);

        result.Should().NotBeNull();
        result!.Kind.Should().Be(SparkplugMessageKind.DData);
        result.GroupId.Should().Be("FactoryA");
        result.EdgeNodeId.Should().Be("Edge1");
        result.DeviceId.Should().Be("PLC1");
        result.HostId.Should().BeNull();
    }

    [Fact]
    public void Parse_Should_Correctly_Identify_State_Topics()
    {
        var topic = "spBv1.0/STATE/SCADA_1";

        var result = TopicParser.Parse(topic);

        result.Should().NotBeNull();
        result!.Kind.Should().Be(SparkplugMessageKind.State);
        result.HostId.Should().Be("SCADA_1");
        result.GroupId.Should().BeNull();
        result.EdgeNodeId.Should().BeNull();
        result.DeviceId.Should().BeNull();
    }

    [Theory]
    [InlineData("wrongPrefix/FactoryA/NBIRTH/Edge1")]
    [InlineData("spBv1.0/FactoryA")] // Too short
    [InlineData("spBv1.0/STATE/SCADA_1/Extra")] // Too long for STATE
    [InlineData("spBv1.0/FactoryA/NBIRTH/Edge1/Device1/Extra")] // Too long for Device
    [InlineData(null)]
    [InlineData("")]
    public void Parse_Should_Return_Null_For_Invalid_Structures(string? invalidTopic)
    {
        var result = TopicParser.Parse(invalidTopic!);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_Should_Return_Unknown_Kind_For_Invalid_Message_Type()
    {
        var topic = "spBv1.0/FactoryA/INVALID_TYPE/Edge1";

        var result = TopicParser.Parse(topic);

        result.Should().NotBeNull();
        result!.Kind.Should().Be(SparkplugMessageKind.Unknown);
        result.GroupId.Should().Be("FactoryA");
        result.EdgeNodeId.Should().Be("Edge1");
    }
}