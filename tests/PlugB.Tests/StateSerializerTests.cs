using FluentAssertions;
using PlugB.Internal.Mapping;

namespace PlugB.Tests;

public class StateSerializerTests
{
    [Fact]
    public void Serialize_Should_Create_Valid_Sparkplug_State_Json()
    {
        // Act
        var resultOnline = StateSerializer.Serialize(online: true, timestamp: 1629837492000);
        var resultOffline = StateSerializer.Serialize(online: false, timestamp: 1629837492000);

        // Assert
        resultOnline.Should().Contain("\"online\":true");
        resultOnline.Should().Contain("\"timestamp\":1629837492000");

        resultOffline.Should().Contain("\"online\":false");
        resultOffline.Should().Contain("\"timestamp\":1629837492000");
    }
}