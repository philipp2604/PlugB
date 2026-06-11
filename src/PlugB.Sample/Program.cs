using Microsoft.Extensions;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using PlugB.Builders;
using PlugB.Internal.State;
using PlugB.Options;
using PlugB.Storage;

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
        var options = new MqttClientOptionsBuilder().WithTcpServer("localhost", 1883).Build();

        await mqttClient.ConnectAsync(options);
        Console.WriteLine("Host connected to MQTT broker.");
        Console.WriteLine("Press 'O' to set host ONLINE, 'F' to set OFFLINE, or 'Q' to quit.");

        while (true)
        {
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.Q) break;

            if (key == ConsoleKey.O || key == ConsoleKey.F)
            {
                bool isOnline = key == ConsoleKey.O;

                var payload = JsonSerializer.Serialize(new
                {
                    online = isOnline,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic("spBv1.0/STATE/SCADA_1")
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(true) // STATE must be retained
                    .Build();

                await mqttClient.PublishAsync(message);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Published STATE: {(isOnline ? "ONLINE" : "OFFLINE")}");
            }
        }
    }

    static async Task RunEdgeNodeAsync(ILogger logger)
    {
        Console.Title = "Edge Node (PlugB)";
        Console.WriteLine("Running as Edge Node. Start the Host simulator in another terminal using '--host' to control the state.");

        // 1. Configure the Client via Builder
        await using var client = new PlugBClientBuilder()
            .WithBroker("localhost", 1883)
            .WithNodeId("Factory_01", "EdgeGateway_A")
            .WithPrimaryHost("SCADA_1") // Enables Birth-Gating
            .WithStoreAndForward(o =>
            {
                o.Capacity = 100_000;
                o.Eviction = EvictionPolicy.DropOldest;
                // Use FileStore to persist data while offline
                o.Store = new FileForwardStore("./plugb-buffer", o.Capacity, o.Eviction);
            })
            .WithNodeMetric("Hardware/CPU", PlugBDataType.Float, 45.5f)
            .WithLogger(logger)
            .Build();

        // Wire up events to visualize the internal state machine
        client.ConnectionStateChanged += (s, state) => Console.WriteLine($"[EVENT] Connection State: {state}");
        client.HostStateChanged += (s, state) => Console.WriteLine($"[EVENT] Host State: {(state.Online ? "ONLINE" : "OFFLINE")} (TS: {state.LastTimestampMs})");
        client.BufferOverflow += (s, info) => Console.WriteLine($"[EVENT] Buffer Overflow! Dropped {info.DroppedCount} metrics.");
        client.HistoricalFlushCompleted += (s, count) => Console.WriteLine($"[EVENT] Successfully flushed {count} historical metrics from Store-and-Forward.");

        var plc1 = client.CreateDevice("PLC_Machine_1");
        plc1.AddBirthMetric("Status", PlugBDataType.String, "Running");

        try
        {
            // 3. Start: Connects -> sets NDEATH as LWT -> WAITS for Host STATE online!
            await client.StartAsync();

            // 4. Publish runtime data in a loop. 
            // If the host is offline, these will go into the Store-and-Forward FileStore.
            int counter = 0;
            while (true)
            {
                await Task.Delay(2000);
                counter++;

                var newData = MetricBuilder.Create("Counter").WithValue(counter).Build();

                // We await it, but it won't throw if offline because StoreAndForward is enabled
                await plc1.PublishDataAsync(newData);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Generated Data: Counter = {counter} (Will be buffered if Host is OFFLINE)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during execution: {ex.Message}");
        }
    }
}
