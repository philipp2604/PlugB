using FluentAssertions;
using Google.Protobuf;
using MQTTnet;
using PlugB.Builders;
using PlugB.Events;
using PlugB.Internal.Mapping;
using PlugB.Models;
using System.Collections.Concurrent;
using Testcontainers.Mosquitto;

namespace PlugB.IntegrationTests;

[Trait("Category", "Integration")]
public class HostConsumerIntegrationTests
{
    [Fact]
    public async Task Host_Should_Consume_Decode_And_Resolve_Aliases_From_Edge()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var broker = new MosquittoBuilder("eclipse-mosquitto:2.0").Build();
        await broker.StartAsync(ct);

        var brokerHost = broker.Hostname;
        var brokerPort = broker.GetMappedPublicPort(1883);

        // 1. Setup Host
        await using var host = (PlugBHost)new PlugBHostBuilder()
            .WithBroker(brokerHost, brokerPort)
            .WithHostId("SCADA_1")
            .Build();

        var nodeBirths = new ConcurrentQueue<NodeBirthEventArgs>();
        var deviceBirths = new ConcurrentQueue<DeviceBirthEventArgs>();
        var dataChanges = new ConcurrentQueue<DataChangedEventArgs>();

        host.NodeBirth += (_, e) => nodeBirths.Enqueue(e);
        host.DeviceBirth += (_, e) => deviceBirths.Enqueue(e);
        host.DataChanged += (_, e) => dataChanges.Enqueue(e);

        await host.StartAsync(ct);

        // 2. Setup Edge Node
        await using var edge = new PlugBClientBuilder()
            .WithBroker(brokerHost, brokerPort)
            .WithNodeId("Group1", "Node1")
            .WithNodeMetric("Node_CPU", PlugBDataType.Double, 45.0)
            .Build();

        var device = edge.CreateDevice("Device1");
        device.AddBirthMetric("Temperature", PlugBDataType.Double, 20.0, alias: 100);

        await edge.StartAsync(ct);

        // 3. Publish DDATA
        // Wait briefly to ensure BIRTHs are processed before sending DATA
        await Task.Delay(1000, ct);

        // Publish metric using only Alias (as it happens in real DDATA payloads)
        var dataMetric = new Metric { Name = string.Empty, Alias = 100, DataType = PlugBDataType.Double, Value = 25.5 };
        await device.PublishDataAsync(dataMetric, ct);

        // 4. Assertions
        await Task.Delay(2000, ct); // allow messages to propagate

        nodeBirths.Should().ContainSingle();
        deviceBirths.Should().ContainSingle();
        dataChanges.Should().ContainSingle("The host should receive and process the DDATA message");

        var snapshot = host.Nodes;
        snapshot.Should().HaveCount(1);

        var nodeSnapshot = snapshot.First();
        nodeSnapshot.GroupId.Should().Be("Group1");
        nodeSnapshot.EdgeNodeId.Should().Be("Node1");
        nodeSnapshot.Metrics["Node_CPU"].Value.Should().Be(45.0);

        var deviceSnapshot = nodeSnapshot.Devices["Device1"];
        deviceSnapshot.Should().NotBeNull();

        // Verify Alias Resolution: Name should be resolved to "Temperature", and value updated to 25.5
        deviceSnapshot.Metrics["Temperature"].Value.Should().Be(25.5);
    }

    [Fact]
    public async Task Host_Should_Request_Rebirth_On_Sequence_Gap_And_Edge_Should_Respond()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var broker = new MosquittoBuilder("eclipse-mosquitto:2.0").Build();
        await broker.StartAsync(ct);

        // Setup Host
        await using var host = (PlugBHost)new PlugBHostBuilder()
            .WithBroker(broker.Hostname, broker.GetMappedPublicPort(1883))
            .WithHostId("SCADA_1")
            .WithRebirthOnGap(true)
            .Build();

        int birthCount = 0;
        var rebirthRequested = new TaskCompletionSource<bool>();
        var secondBirthReceived = new TaskCompletionSource<bool>();

        host.NodeBirth += (_, e) =>
        {
            birthCount++;
            if (birthCount == 2) secondBirthReceived.TrySetResult(true);
        };

        host.RebirthRequested += (_, e) => rebirthRequested.TrySetResult(true);

        await host.StartAsync(ct);

        // Setup Edge Node
        await using var edge = new PlugBClientBuilder()
            .WithBroker(broker.Hostname, broker.GetMappedPublicPort(1883))
            .WithNodeId("Group1", "Node1")
            .Build();

        await edge.StartAsync(ct);
        await Task.Delay(1000, ct); // Wait for initial NBIRTH
        birthCount.Should().Be(1);

        // Inject a raw out-of-sequence DDATA message to simulate a gap
        var factory = new MqttClientFactory();
        using var rawClient = factory.CreateMqttClient();
        await rawClient.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(broker.Hostname, broker.GetMappedPublicPort(1883)).Build(), ct);

        var badMetric = MetricBuilder.Create("FakeData").WithValue(1).Build();
        var badPayload = PayloadBuilder.Build([badMetric], seq: 200, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); // seq 200 forces a gap
        var badTopic = TopicGenerator.GetDeviceTopic("Group1", "Node1", "Dev1", TopicGenerator.MsgTypeDeviceData);

        var badMsg = new MqttApplicationMessageBuilder()
            .WithTopic(badTopic)
            .WithPayload(badPayload.ToByteArray())
            .Build();

        await rawClient.PublishAsync(badMsg, ct);

        // Host should detect the gap and fire the RebirthRequested event
        var rebirthTimeout = await Task.WhenAny(rebirthRequested.Task, Task.Delay(5000, ct));
        rebirthTimeout.Should().Be(rebirthRequested.Task, "Host did not request a rebirth within timeout.");

        // Edge should process the NCMD and publish a new NBIRTH
        var birthTimeout = await Task.WhenAny(secondBirthReceived.Task, Task.Delay(5000, ct));
        birthTimeout.Should().Be(secondBirthReceived.Task, "Edge node did not publish a new NBIRTH after receiving the command.");

        await rawClient.DisconnectAsync(cancellationToken: ct);
    }

    [Fact]
    public async Task Edge_Should_Gate_Birth_Until_Host_Publishes_State()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var broker = new MosquittoBuilder("eclipse-mosquitto:2.0").Build();
        await broker.StartAsync(ct);

        // Setup Edge Node with PrimaryHost defined
        await using var edge = new PlugBClientBuilder()
            .WithBroker(broker.Hostname, broker.GetMappedPublicPort(1883))
            .WithNodeId("Group1", "Node1")
            .WithPrimaryHost("SCADA_MAIN")
            .Build();

        await edge.StartAsync(ct);

        // Wait a moment to prove the Edge does NOT send its birth yet
        await Task.Delay(2000, ct);

        var hostBirthReceived = new TaskCompletionSource<bool>();

        // Setup Host Application matching the PrimaryHost ID
        await using var host = (PlugBHost)new PlugBHostBuilder()
            .WithBroker(broker.Hostname, broker.GetMappedPublicPort(1883))
            .WithHostId("SCADA_MAIN")
            .Build();

        host.NodeBirth += (_, _) => hostBirthReceived.TrySetResult(true);

        // Starting the host will publish the STATE message (online=true)
        await host.StartAsync(ct);

        // The Edge should detect the STATE change and release its NBIRTH
        var completed = await Task.WhenAny(hostBirthReceived.Task, Task.Delay(5000, ct));
        completed.Should().Be(hostBirthReceived.Task, "Edge should have published its NBIRTH after Host came online.");
    }
}