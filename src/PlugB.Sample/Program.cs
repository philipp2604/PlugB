using Microsoft.Extensions;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using PlugB.Builders;
using PlugB.Internal.State;
using PlugB.Options;
using PlugB.Storage;
using ProtoPayload = Com.Cirruslink.Sparkplug.Protobuf.Payload;

namespace PlugB.Sample;

internal class Program
{
    static async Task Main(string[] args)
    {
        // Setup a simple console logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("PlugB", LogLevel.Debug);
            builder.AddConsole();
        });
        var logger = loggerFactory.CreateLogger("PlugB.Sample");

        Console.WriteLine("=== PlugB Sparkplug-B V3.0 Demo ===");

        if (args.Contains("--host", StringComparer.OrdinalIgnoreCase))
        {
            await RunHostSimulatorAsync();
        }
        else
        {
            await RunEdgeNodeAsync(logger);
        }
    }

    static async Task RunHostSimulatorAsync()
    {
        Console.Title = "Primary Host (SCADA_1)";
        Console.WriteLine("Running as Primary Host Simulator (SCADA_1)...");

        var factory = new MqttClientFactory();
        using var mqttClient = factory.CreateMqttClient();

        // 1. Generate ONE base timestamp for this entire connection session
        long sessionTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 2. The LWT MUST be exactly 1 ms newer than the LIVE message, 
        // so it always overrides the LIVE message if the connection drops!
        var offlinePayload = JsonSerializer.Serialize(new
        {
            online = false,
            timestamp = sessionTimestamp + 1
        });

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer("localhost", 1883)
            .WithWillTopic("spBv1.0/STATE/SCADA_1")
            .WithWillPayload(offlinePayload)
            .WithWillQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain(true) // STATE must be retained
            .Build();

        mqttClient.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            if (topic.StartsWith("spBv1.0/STATE/")) return Task.CompletedTask;

            try
            {
                var payload = ProtoPayload.Parser.ParseFrom(e.ApplicationMessage.Payload);
                var metricInfo = string.Join(", ", payload.Metric.Select(m => $"{m.Name} {(m.IsHistorical ? "[HIST]" : "")}"));
                Console.WriteLine($"\n>> [RECEIVED] Topic: {topic} | Seq: {payload.Seq} | Metrics: {metricInfo}");
            }
            catch { }
            return Task.CompletedTask;
        };

        await mqttClient.ConnectAsync(options);

        await mqttClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("spBv1.0/Factory_01/#")
            .Build());

        Console.WriteLine("Host connected to MQTT broker and listening for Edge Node data.");
        Console.WriteLine("===============================================================");
        Console.WriteLine("Press 'O' to set host ONLINE, 'F' to set OFFLINE, or 'Q' to quit.");
        Console.WriteLine("===============================================================");

        while (true)
        {
            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.Q)
            {
                // Graceful disconnect: Publish a brand new OFFLINE message with current time
                var payload = JsonSerializer.Serialize(new { online = false, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                var message = new MqttApplicationMessageBuilder().WithTopic("spBv1.0/STATE/SCADA_1").WithPayload(payload).WithRetainFlag(true).Build();
                await mqttClient.PublishAsync(message);
                break;
            }
            else if (key == ConsoleKey.O)
            {
                // Live message uses the exact session timestamp (which is 1ms OLDER than the LWT)
                var payload = JsonSerializer.Serialize(new { online = true, timestamp = sessionTimestamp });
                var message = new MqttApplicationMessageBuilder().WithTopic("spBv1.0/STATE/SCADA_1").WithPayload(payload).WithRetainFlag(true).Build();
                await mqttClient.PublishAsync(message);
                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ---> PUBLISHED HOST STATE: ONLINE (TS: {sessionTimestamp})");
            }
            else if (key == ConsoleKey.F)
            {
                // Manual OFFLINE uses a fresh timestamp to override everything
                long freshTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var payload = JsonSerializer.Serialize(new { online = false, timestamp = freshTs });
                var message = new MqttApplicationMessageBuilder().WithTopic("spBv1.0/STATE/SCADA_1").WithPayload(payload).WithRetainFlag(true).Build();
                await mqttClient.PublishAsync(message);
                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ---> PUBLISHED HOST STATE: OFFLINE (TS: {freshTs})");
            }
        }
    }

    static async Task RunEdgeNodeAsync(ILogger logger)
    {
        Console.Title = "Edge Node (PlugB)";
        Console.WriteLine("Running as Edge Node.");
        Console.WriteLine("Start the Host simulator in another terminal using 'dotnet run --host' to control the state.");
        Console.WriteLine("===============================================================================================");

        // 1. Configure the Client via Builder
        await using var client = new PlugBClientBuilder()
            .WithBroker("localhost", 1883)
            .WithNodeId("Factory_01", "EdgeGateway_A")
            .WithPrimaryHost("SCADA_1") // Enables Birth-Gating
            .WithStoreAndForward(o =>
            {
                o.Capacity = 100_000;
                o.Eviction = EvictionPolicy.DropOldest;
                o.Store = new FileForwardStore("./plugb-buffer", o.Capacity, o.Eviction);
            })
            .WithNodeMetric("Hardware/CPU", PlugBDataType.Float, 45.5f)
            .WithLogger(logger)
            .Build();

        // Wire up events to visualize the internal state machine
        client.ConnectionStateChanged += (s, state) => Console.WriteLine($"\n[STATE] Connection: {state}");
        client.HostStateChanged += (s, state) => Console.WriteLine($"[STATE] Host: {(state.Online ? "ONLINE" : "OFFLINE")} (TS: {state.LastTimestampMs})");
        client.BufferOverflow += (s, info) => Console.WriteLine($"[WARN] Buffer Overflow! Dropped {info.DroppedCount} metrics.");
        client.HistoricalFlushCompleted += (s, count) => Console.WriteLine($"[INFO] Successfully flushed {count} historical metrics to the broker!");

        var plc1 = client.CreateDevice("PLC_Machine_1");
        plc1.AddBirthMetric("Status", PlugBDataType.String, "Running");

        try
        {
            // 3. Start: Connects -> sets NDEATH as LWT -> WAITS for Host STATE online!
            await client.StartAsync();

            // 4. Publish runtime data in a loop.
            int counter = 0;
            while (true)
            {
                await Task.Delay(2000);
                counter++;

                var newData = MetricBuilder.Create("Counter").WithValue(counter).Build();

                // Try publishing. StoreAndForward will route it to disk if not ONLINE.
                await plc1.PublishDataAsync(newData);

                if (client.ConnectionState == PlugBConnectionState.Online)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Published LIVE: Counter = {counter}");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Buffered to FileStore (State: {client.ConnectionState}): Counter = {counter}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during execution: {ex.Message}");
        }
    }
}
