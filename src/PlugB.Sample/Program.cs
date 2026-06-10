using Microsoft.Extensions.Logging;
using PlugB.Builders;
using PlugB.Options;
namespace PlugB.Sample
{
    internal class Program
    {
        static async Task Main()
        {

            // Setup a simple console logger
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter("PlugB", LogLevel.Debug);
                builder.AddConsole();
            });
            var logger = loggerFactory.CreateLogger("PlugB");

            Console.WriteLine("Starting PlugB Sparkplug-B Edge Node...");

            // 1. Configure the Client via Builder
            await using var client = new PlugBClientBuilder()
                .WithBroker("localhost", 1883)
                .WithNodeId("Factory_01", "EdgeGateway_A")
                // Optional: .WithPrimaryHost("SCADA_1")
                // Attach NBIRTH metrics directly in the builder
                .WithNodeMetric("Hardware/CPU", PlugBDataType.Float, 45.5f)
                .WithLogger(logger)
                .Build();

            // 2. Create a Device
            var plc1 = client.CreateDevice("PLC_Machine_1");
            plc1.AddBirthMetric("Status", PlugBDataType.String, "Running");
            plc1.AddBirthMetric("Temperature", PlugBDataType.Double, 22.1);

            try
            {
                // 3. Start: Connects -> sets NDEATH as LWT -> sends NBIRTH -> sends DBIRTH(plc1)
                await client.StartAsync();
                Console.WriteLine("Client connected and birth certificates published.");

                // 4. Publish runtime data (Wrapper handles DDATA + seq numbering)
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(2000); // Simulate work

                    var simulatedTemp = 22.1 + (i * 0.5);
                    var newData = MetricBuilder.Create("Temperature").WithValue(simulatedTemp).Build();

                    await plc1.PublishDataAsync(newData);
                    Console.WriteLine($"Published DDATA: Temperature = {simulatedTemp}");
                }

                Console.WriteLine("Press any key to gracefully disconnect...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during execution: {ex.Message}");
            }

            // 5. Clean shutdown is handled 'await using' which calls client.DisposeAsync()
        }
    }
}
