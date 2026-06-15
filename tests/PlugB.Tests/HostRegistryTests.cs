using FluentAssertions;
using PlugB.Builders;
using PlugB.Internal.State;
using PlugB.Models;

namespace PlugB.Tests;

public class HostRegistryTests
{
    [Fact]
    public void Registry_Should_Build_AliasMap_And_Resolve_Data()
    {
        // Arrange
        var registry = new HostNodeRegistry();
        var birthMetrics = new[]
        {
            MetricBuilder.Create("Temperature").WithValue(22.5, PlugBDataType.Double).WithAlias(100).Build()
        };

        // Act - BIRTH
        var node = registry.ProcessNBirth("G1", "E1", 1, birthMetrics);

        // Assert BIRTH
        node.Online.Should().BeTrue();
        node.Metrics.Should().ContainKey("Temperature");
        node.Metrics["Temperature"].Alias.Should().Be(100);

        // Act - DATA (using only Alias, no Name)
        var dataMetrics = new[]
        {
            new Metric { Name = string.Empty, Alias = 100, DataType = PlugBDataType.Double, Value = 25.0 }
        };
        var (result, snapshot) = registry.ProcessNData("G1", "E1", dataMetrics);

        // Assert DATA
        result.Should().Be(DataProcessingResult.Success);
        snapshot.Should().NotBeNull();
        snapshot!.Metrics["Temperature"].Value.Should().Be(25.0);
    }

    [Fact]
    public void Registry_Should_Return_UnknownAlias_If_Alias_Is_Missing()
    {
        // Arrange
        var registry = new HostNodeRegistry();
        registry.ProcessNBirth("G1", "E1", 1, []);

        // Act - DATA with unknown Alias
        var dataMetrics = new[]
        {
            new Metric { Name = string.Empty, Alias = 999, DataType = PlugBDataType.Double, Value = 0 }
        };
        var (result, snapshot) = registry.ProcessNData("G1", "E1", dataMetrics);

        // Assert
        result.Should().Be(DataProcessingResult.UnknownAlias);
        snapshot.Should().BeNull();
    }

    [Fact]
    public void Registry_Should_Manage_Device_Lifecycles()
    {
        var registry = new HostNodeRegistry();
        registry.ProcessNBirth("G1", "E1", 1, []);

        // Act - Device Birth
        registry.ProcessDBirth("G1", "E1", "D1", []);

        var nodes = registry.GetNodes();
        nodes.Single().Devices.Should().ContainKey("D1");
        nodes.Single().Devices["D1"].Online.Should().BeTrue();

        // Act - Device Death
        registry.ProcessDDeath("G1", "E1", "D1");

        nodes = registry.GetNodes();
        nodes.Single().Devices["D1"].Online.Should().BeFalse();
    }
}