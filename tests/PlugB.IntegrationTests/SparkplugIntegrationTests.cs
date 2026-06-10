using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MQTTnet;
using PlugB.Builders;
using PlugB.Options;
using Testcontainers.Mosquitto;
using Xunit;
using ProtoPayload = Com.Cirruslink.Sparkplug.Protobuf.Payload;

namespace PlugB.IntegrationTests;

[Trait("Category", "Integration")]
public class SparkplugIntegrationTests : IAsyncLifetime
{
    private readonly MosquittoContainer _mosquittoContainer = new MosquittoBuilder("eclipse-mosquitto:2.0")
        .Build();

    public ValueTask InitializeAsync() => new(_mosquittoContainer.StartAsync());

    public async ValueTask DisposeAsync()
    {
        await _mosquittoContainer.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Client_Should_Connect_And_Publish_Valid_NBirth_And_DBirth()
    {
        var ct = TestContext.Current.CancellationToken;

        var brokerHost = _mosquittoContainer.Hostname;
        var brokerPort = _mosquittoContainer.GetMappedPublicPort(1883);

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
}