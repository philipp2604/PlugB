using FluentAssertions;
using PlugB.Builders;
using PlugB.Internal.Domain;
using PlugB.Internal.State;
using PlugB.Internal.Transport;
using PlugB.Models;
using System.Reflection;

namespace PlugB.Tests;

public class PlugBClientTests
{
    [Fact]
    public void ConnectionStateChanged_Event_Should_Raise_Up_From_Internal_StateMachine()
    {
        // Arrange
        var client = (PlugBClient)new PlugBClientBuilder()
            .WithBroker("localhost")
            .WithNodeId("Group1", "Node1")
            .Build();

        // get internal state machine
        var stateMachineField = typeof(PlugBClient).GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance);
        var stateMachine = (ConnectionStateMachine)stateMachineField!.GetValue(client)!;

        var receivedStates = new List<PlugBConnectionState>();
        client.ConnectionStateChanged += (sender, state) => receivedStates.Add(state);

        // Act
        stateMachine.TransitionTo(PlugBConnectionState.ConnectedAwaitingHost);
        stateMachine.TransitionTo(PlugBConnectionState.Online);

        // Assert
        receivedStates.Should().HaveCount(2);
        receivedStates[0].Should().Be(PlugBConnectionState.ConnectedAwaitingHost);
        receivedStates[1].Should().Be(PlugBConnectionState.Online);

        // Property should match the current state
        client.ConnectionState.Should().Be(PlugBConnectionState.Online);
    }

    [Fact]
    public void HostStateChanged_Event_Should_Raise_Up_From_Internal_HostMonitor()
    {
        // Arrange
        var client = (PlugBClient)new PlugBClientBuilder()
            .WithBroker("localhost")
            .WithNodeId("Group1", "Node1")
            .WithPrimaryHost("SCADA_1")
            .Build();

        // get internal monitor
        var hostMonitorField = typeof(PlugBClient).GetField("_hostMonitor", BindingFlags.NonPublic | BindingFlags.Instance);
        var hostMonitor = (PrimaryHostMonitor)hostMonitorField!.GetValue(client)!;

        var receivedHostStates = new List<PrimaryHostState>();
        client.HostStateChanged += (sender, state) => receivedHostStates.Add(state);

        // Act
        // simulate receiving a valid STATE message
        hostMonitor.ProcessStateMessage(new StateMessage(Online: true, TimestampMs: 1000));

        // simulate receiving a stale message (should not trigger)
        hostMonitor.ProcessStateMessage(new StateMessage(Online: false, TimestampMs: 500));

        // simulate host going offline with a newer timestamp
        hostMonitor.ProcessStateMessage(new StateMessage(Online: false, TimestampMs: 2000));

        // Assert
        receivedHostStates.Should().HaveCount(2, "The stale message must not trigger a state change event");

        receivedHostStates[0].Online.Should().BeTrue();
        receivedHostStates[0].LastTimestampMs.Should().Be(1000);

        receivedHostStates[1].Online.Should().BeFalse();
        receivedHostStates[1].LastTimestampMs.Should().Be(2000);

        // Property should match the current known state
        client.HostState.Online.Should().BeFalse();
        client.HostState.Known.Should().BeTrue();
    }

    [Fact]
    public void CreateDevice_Should_Add_Device_To_Internal_Registry()
    {
        // Arrange
        var client = new PlugBClientBuilder()
            .WithBroker("localhost")
            .WithNodeId("Group1", "Node1")
            .Build();

        // Act
        var device1 = client.CreateDevice("Sensor_A");

        // Assert
        device1.Should().NotBeNull();
        device1.DeviceId.Should().Be("Sensor_A");

        // duplicate device IDs should throw an exception (handled by the internal DeviceRegistry)
        Action createDuplicate = () => client.CreateDevice("Sensor_A");
        createDuplicate.Should().Throw<ArgumentException>()
            .WithMessage("*already registered*");
    }
}