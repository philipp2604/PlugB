using FluentAssertions;
using MQTTnet;
using PlugB.Builders;
using PlugB.Options;
using PlugB.Storage;
using System.Collections.Concurrent;
using System.Text.Json;
using Testcontainers.Mosquitto;
using ProtoPayload = Com.Cirruslink.Sparkplug.Protobuf.Payload;

namespace PlugB.IntegrationTests;

[Trait("Category", "Integration")]
public class SparkplugIntegrationTests
{
    [Fact]
    public async Task Client_Should_Connect_And_Publish_Valid_NBirth_And_DBirth()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var broker = new MosquittoBuilder("eclipse-mosquitto:2.0").Build();
        await broker.StartAsync(ct);

        var brokerHost = broker.Hostname;
        var brokerPort = broker.GetMappedPublicPort(1883);

        var factory = new MqttClientFactory();
        using var monitorClient = factory.CreateMqttClient();

        var monitorOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId("Test_Monitor_Client")
            .Build();

        await monitorClient.ConnectAsync(monitorOptions, ct);

        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("spBv1.0/TestGroup/#")
            .Build();

        await monitorClient.SubscribeAsync(subscribeOptions, ct);

        var nBirthReceived = new TaskCompletionSource<ProtoPayload>();
        var dBirthReceived = new TaskCompletionSource<ProtoPayload>();

        monitorClient.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = ProtoPayload.Parser.ParseFrom(e.ApplicationMessage.Payload);

            if (topic.Contains("/NBIRTH/"))
            {
                nBirthReceived.TrySetResult(payload);
            }
            else if (topic.Contains("/DBIRTH/"))
            {
                dBirthReceived.TrySetResult(payload);
            }

            return Task.CompletedTask;
        };

        await using var plugBClient = new PlugBClientBuilder()
            .WithBroker(brokerHost, brokerPort)
            .WithNodeId("TestGroup", "TestNode")
            .WithNodeMetric("Node_CPU", PlugBDataType.Double, 42.5)
            .Build();

        var device = plugBClient.CreateDevice("TestDevice");
        device.AddBirthMetric("Device_Status", PlugBDataType.String, "Online");

        await plugBClient.StartAsync(ct);

        var timeout = Task.Delay(TimeSpan.FromSeconds(5), ct);
        var completedTask = await Task.WhenAny(Task.WhenAll(nBirthReceived.Task, dBirthReceived.Task), timeout);

        completedTask.Should().NotBe(timeout, "The NBIRTH and DBIRTH messages should have been received within the timeout.");

        var nBirthMessage = await nBirthReceived.Task;
        var dBirthMessage = await dBirthReceived.Task;

        nBirthMessage.Seq.Should().Be(0ul, "NBIRTH must have a sequence number of 0");

        var bdSeqMetric = nBirthMessage.Metric.FirstOrDefault(m => m.Name == "bdSeq");
        bdSeqMetric.Should().NotBeNull("NBIRTH must contain the bdSeq metric");
        bdSeqMetric!.LongValue.Should().BeGreaterThanOrEqualTo(0ul);

        var cpuMetric = nBirthMessage.Metric.FirstOrDefault(m => m.Name == "Node_CPU");
        cpuMetric.Should().NotBeNull();
        cpuMetric!.DoubleValue.Should().Be(42.5);

        dBirthMessage.Seq.Should().Be(1ul, "DBIRTH should increment the sequence number to 1");

        var statusMetric = dBirthMessage.Metric.FirstOrDefault(m => m.Name == "Device_Status");
        statusMetric.Should().NotBeNull();
        statusMetric!.StringValue.Should().Be("Online");

        await monitorClient.DisconnectAsync(cancellationToken: ct);
    }

    [Fact]
    public async Task Should_Gate_Birth_Until_Primary_Host_Is_Online()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var broker = new MosquittoBuilder("eclipse-mosquitto:2.0").Build();
        await broker.StartAsync(ct);

        var factory = new MqttClientFactory();
        using var monitorClient = factory.CreateMqttClient();
        await monitorClient.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(broker.Hostname, broker.GetMappedPublicPort(1883))
            .WithClientId("Monitor")
            .Build(), ct);
        await monitorClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("spBv1.0/TestGroup/#").Build(), ct);

        int birthCount = 0;
        monitorClient.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic.Contains("NBIRTH")) Interlocked.Increment(ref birthCount);
            return Task.CompletedTask;
        };

        await using var plugBClient = new PlugBClientBuilder()
            .WithBroker(broker.Hostname, broker.GetMappedPublicPort(1883))
            .WithNodeId("TestGroup", "TestNode")
            .WithPrimaryHost("SCADA_1")
            .Build();

        await plugBClient.StartAsync(ct);
        await Task.Delay(2000, ct);

        birthCount.Should().Be(0, "Rule P2: NBIRTH must be gated while Primary Host is unknown.");

        var statePayload = JsonSerializer.Serialize(new { online = true, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        var stateMsg = new MqttApplicationMessageBuilder()
            .WithTopic("spBv1.0/STATE/SCADA_1")
            .WithPayload(statePayload)
            .WithRetainFlag(true)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await monitorClient.PublishAsync(stateMsg, ct);

        await Task.Delay(1000, ct);
        birthCount.Should().Be(1, "NBIRTH must be published exactly once after host goes ONLINE.");

        await monitorClient.DisconnectAsync(cancellationToken: ct);
    }

    [Fact]
    public async Task Should_Buffer_While_Offline_And_Flush_Historical_Data_In_Order()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var broker = new MosquittoBuilder("eclipse-mosquitto:2.0").Build();
        await broker.StartAsync(ct);

        var factory = new MqttClientFactory();
        using var monitorClient = factory.CreateMqttClient();
        await monitorClient.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(broker.Hostname, broker.GetMappedPublicPort(1883)).Build(), ct);
        await monitorClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("spBv1.0/TestGroup/#").Build(), ct);

        var receivedTopics = new ConcurrentQueue<string>();
        var flushedPayload = new TaskCompletionSource<ProtoPayload>();

        monitorClient.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            if (topic.Contains("BIRTH") || topic.Contains("DDATA"))
            {
                receivedTopics.Enqueue(topic);
            }
            if (topic.Contains("DDATA"))
            {
                flushedPayload.TrySetResult(ProtoPayload.Parser.ParseFrom(e.ApplicationMessage.Payload));
            }
            return Task.CompletedTask;
        };

        await using var plugBClient = new PlugBClientBuilder()
            .WithBroker(broker.Hostname, broker.GetMappedPublicPort(1883))
            .WithNodeId("TestGroup", "TestNode")
            .WithPrimaryHost("SCADA_1")
            .WithStoreAndForward(o => o.Store = new InMemoryForwardStore(100, EvictionPolicy.DropOldest))
            .Build();

        var device = plugBClient.CreateDevice("Device1");
        device.AddBirthMetric("Status", PlugBDataType.String, "On");
        await plugBClient.StartAsync(ct);

        var historicalMetric = MetricBuilder.Create("Temp").WithValue(22.5).Build();
        await device.PublishDataAsync(historicalMetric, ct);

        var statePayload = JsonSerializer.Serialize(new { online = true, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        var stateMsg = new MqttApplicationMessageBuilder().WithTopic("spBv1.0/STATE/SCADA_1").WithPayload(statePayload).WithRetainFlag(true).Build();
        await monitorClient.PublishAsync(stateMsg, ct);

        var timeout = Task.Delay(TimeSpan.FromSeconds(5), ct);
        var completed = await Task.WhenAny(flushedPayload.Task, timeout);
        completed.Should().NotBe(timeout, "The buffered DDATA should have been flushed.");

        var topics = receivedTopics.ToArray();
        topics.Length.Should().Be(3);
        topics[0].Should().Contain("NBIRTH");
        topics[1].Should().Contain("DBIRTH");
        topics[2].Should().Contain("DDATA");

        var data = await flushedPayload.Task;
        data.Seq.Should().Be(2ul, "Seq must be fresh (0 for NBIRTH, 1 for DBIRTH, 2 for DDATA).");
        var tempMetric = data.Metric.First(m => m.Name == "Temp");
        tempMetric.IsHistorical.Should().BeTrue("Buffered data MUST be marked as historical.");

        await monitorClient.DisconnectAsync(cancellationToken: ct);
    }

    [Fact]
    public async Task Should_Failover_To_Next_Server_When_Current_Broker_Dies()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var broker1 = new MosquittoBuilder("eclipse-mosquitto:2.0").Build();
        await using var broker2 = new MosquittoBuilder("eclipse-mosquitto:2.0").Build();
        await broker1.StartAsync(ct);
        await broker2.StartAsync(ct);

        var factory = new MqttClientFactory();
        using var monitorOnBroker2 = factory.CreateMqttClient();
        await monitorOnBroker2.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(broker2.Hostname, broker2.GetMappedPublicPort(1883))
            .WithClientId("Monitor2")
            .Build(), ct);
        await monitorOnBroker2.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter("spBv1.0/TestGroup/#").Build(), ct);

        var nBirthReceivedOnBroker2 = new TaskCompletionSource<ProtoPayload>();
        monitorOnBroker2.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic.Contains("NBIRTH"))
            {
                nBirthReceivedOnBroker2.TrySetResult(ProtoPayload.Parser.ParseFrom(e.ApplicationMessage.Payload));
            }
            return Task.CompletedTask;
        };

        await using var plugBClient = new PlugBClientBuilder()
            .WithBrokers(
                new MqttBroker(broker1.Hostname, broker1.GetMappedPublicPort(1883)),
                new MqttBroker(broker2.Hostname, broker2.GetMappedPublicPort(1883))
            )
            .WithNodeId("TestGroup", "FailoverNode")
            .Build();

        await plugBClient.StartAsync(ct);
        await Task.Delay(1500, ct);

        await broker1.StopAsync(ct);

        var timeout = Task.Delay(TimeSpan.FromSeconds(10), ct);
        var completedTask = await Task.WhenAny(nBirthReceivedOnBroker2.Task, timeout);

        completedTask.Should().NotBe(timeout, "PlugB should have automatically failed over to Broker 2.");

        var payload = await nBirthReceivedOnBroker2.Task;
        payload.Seq.Should().Be(0ul, "A failover reconnect must force a fresh NBIRTH with seq=0.");

        await monitorOnBroker2.DisconnectAsync(cancellationToken: ct);
    }
}