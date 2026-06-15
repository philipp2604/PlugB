using Microsoft.Extensions.Logging;
using PlugB.Builders;
using PlugB.Models;
using PlugB.Options;
using PlugB.Storage;

namespace PlugB.Sample;

internal class Program
{
    private static async Task Main(string[] args)
    {
        // Setup a simple console logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("PlugB", LogLevel.Information); // Changed to Info to keep console clean
            builder.AddConsole();
        });
        var logger = loggerFactory.CreateLogger("PlugB.Sample");

        Console.WriteLine("=== PlugB Sparkplug-B V3.0 Demo ===");

        if (args.Contains("--host", StringComparer.OrdinalIgnoreCase))
        {
            await RunHostSimulatorAsync(logger);
        }
        else
        {
            await RunEdgeNodeAsync(logger);
        }
    }

    private static async Task RunHostSimulatorAsync(ILogger logger)
    {
        Console.Title = "Primary Host (SCADA_1)";
        Console.WriteLine("Running as Primary Host Application (SCADA_1)...");

        // 1. Configure the Host via Builder
        await using var host = new PlugBHostBuilder()
            .WithBroker("localhost", 1883)
            .WithHostId("SCADA_1")
            .WithGroupFilter("Factory_01")
            .WithRebirthOnGap(true)
            .WithLogger(logger)
            .Build();

        // 2. Wire up Host events
        host.ConnectionChanged += (s, connected) =>
            Console.WriteLine($"\n[HOST] Connection State: {(connected ? "Connected" : "Disconnected")}");

        host.NodeBirth += (s, e) =>
            Console.WriteLine($"\n[HOST] NBIRTH received for {e.GroupId}/{e.EdgeNodeId}. Metrics: {string.Join(", ", e.Metrics.Keys)}");

        host.DeviceBirth += (s, e) =>
            Console.WriteLine($"\n[HOST] DBIRTH received for {e.GroupId}/{e.EdgeNodeId}/{e.DeviceId}. Metrics: {string.Join(", ", e.Metrics.Keys)}");

        host.DataChanged += (s, e) =>
        {
            var level = e.DeviceId == null ? "NODE" : $"DEVICE({e.DeviceId})";
            var values = string.Join(", ", e.Metrics.Select(m => $"{m.Name}={m.Value}"));
            Console.WriteLine($"\n[HOST] DATA ({level}) from {e.GroupId}/{e.EdgeNodeId}: {values}");
        };

        host.NodeDeath += (s, e) => Console.WriteLine($"\n[HOST] NDEATH received for {e.GroupId}/{e.EdgeNodeId}");
        host.DeviceDeath += (s, e) => Console.WriteLine($"\n[HOST] DDEATH received for {e.GroupId}/{e.EdgeNodeId}/{e.DeviceId}");
        host.RebirthRequested += (s, e) => Console.WriteLine($"\n[HOST] Auto-Rebirth requested for {e.GroupId}/{e.EdgeNodeId}");
        host.DecodeFailed += (s, e) => Console.WriteLine($"\n[HOST] Decode failed for topic {e.Topic}: {e.Error.Message}");

        // 3. Start: Connects -> registers STATE LWT -> publishes STATE online -> subscribes
        await host.StartAsync();

        Console.WriteLine("Host connected to MQTT broker and listening for Edge Node data.");
        Console.WriteLine("===============================================================");
        Console.WriteLine("Press 'R' to send a Rebirth Request, 'S' for a snapshot, or 'Q' to quit.");
        Console.WriteLine("===============================================================");

        while (true)
        {
            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.Q)
            {
                // Graceful disconnect: IPlugBHost.DisposeAsync() will automatically publish the offline STATE
                Console.WriteLine("\nShutting down Host... (Publishing STATE Offline)");
                break;
            }
            else if (key == ConsoleKey.R)
            {
                Console.WriteLine("\n[HOST] Requesting Rebirth for Factory_01/EdgeGateway_A...");
                await host.RequestRebirthAsync("Factory_01", "EdgeGateway_A");
            }
            else if (key == ConsoleKey.S)
            {
                Console.WriteLine("\n--- [HOST] Current Namespace Snapshot ---");
                foreach (var node in host.Nodes)
                {
                    Console.WriteLine($"- Node: {node.GroupId}/{node.EdgeNodeId} [Online: {node.Online}, bdSeq: {node.BdSeq}]");
                    foreach (var m in node.Metrics.Values) Console.WriteLine($"    Metric: {m.Name} = {m.Value}");

                    foreach (var dev in node.Devices.Values)
                    {
                        Console.WriteLine($"  - Device: {dev.DeviceId} [Online: {dev.Online}]");
                        foreach (var m in dev.Metrics.Values) Console.WriteLine($"      Metric: {m.Name} = {m.Value}");
                    }
                }
                Console.WriteLine("-----------------------------------------");
            }
        }
    }

    private static async Task RunEdgeNodeAsync(ILogger logger)
    {
        Console.Title = "Edge Node (PlugB)";
        Console.WriteLine("Running as Edge Node.");
        Console.WriteLine("Start the Host application in another terminal using 'dotnet run --host' to control the state.");
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