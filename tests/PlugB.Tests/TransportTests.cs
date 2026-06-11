using FluentAssertions;
using PlugB.Internal.Transport;
using PlugB.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace PlugB.Tests
{
    public class TransportTests
    {
        [Fact]
        public void ServerSelector_Should_RoundRobin_Servers()
        {
            // Arrange
            var servers = new List<MqttBroker>
            {
                new("broker1", 1883),
                new("broker2", 1883)
            };

            var selector = new ServerSelector(servers);

            // Act & Assert
            selector.GetCurrent().Address.Should().Be("broker1");

            selector.MoveNext();
            selector.GetCurrent().Address.Should().Be("broker2");

            selector.MoveNext();
            selector.GetCurrent().Address.Should().Be("broker1", "It must round-robin and wrap around.");
        }

        [Fact]
        public void StateMachine_Should_Start_Disconnected_And_Follow_Transitions()
        {
            // Arrange
            var stateMachine = new ConnectionStateMachine();
            var recordedStates = new List<PlugBConnectionState>();
            stateMachine.StateChanged += (s, e) => recordedStates.Add(e);

            // Act
            stateMachine.TransitionTo(PlugBConnectionState.ConnectedAwaitingHost); // connect with PHID
            stateMachine.TransitionTo(PlugBConnectionState.Online); // received STATE online=true
            stateMachine.TransitionTo(PlugBConnectionState.ConnectedHostOffline); // host goes offline
            stateMachine.TransitionTo(PlugBConnectionState.Disconnected); // failover trigger

            // Assert
            recordedStates.Should().HaveCount(4);
            recordedStates[0].Should().Be(PlugBConnectionState.ConnectedAwaitingHost);
            recordedStates[1].Should().Be(PlugBConnectionState.Online);
            recordedStates[2].Should().Be(PlugBConnectionState.ConnectedHostOffline);
            recordedStates[3].Should().Be(PlugBConnectionState.Disconnected);
        }
    }
}
