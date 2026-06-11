# PlugB 💥🔌

A clean, type-safe C# library for publishing industrial data as a **Sparkplug B Edge Node** —
without touching MQTT topics, QoS levels, retained flags, Protobuf byte arrays, or sequence
numbers.

PlugB wraps [MQTTnet](https://github.com/dotnet/MQTTnet) and the Eclipse Tahu Sparkplug B
payload schema behind a small fluent API and handles the parts hand-rolled Sparkplug wrappers
usually get wrong: the **NBIRTH/NDEATH/DBIRTH lifecycle**, the **`seq`/`bdSeq` sequence
management**, and **thread-safe ordered publishing**. It can also wait for a **Primary Host**,
**fail over** across multiple brokers, and **buffer data** while offline.

## Installation

```bash
dotnet add package philipp2604.PlugB
```

## Quickstart

```csharp
using PlugB;

// 1. Configure the Edge Node via the fluent builder
IPlugBClient client = new PlugBClientBuilder()
    .WithBroker("mqtt.server.local", 1883)
    .WithNodeId("Factory_01", "EdgeGateway_A")
    .WithNodeMetric("Hardware/CPU", PlugBDataType.Float, 45.5f)
    .Build();

// 2. Create a device (its birth/death state is managed internally)
IPlugBDevice plc1 = client.CreateDevice("PLC_Machine_1");
plc1.AddBirthMetric("Status", PlugBDataType.String, "Running");
plc1.AddBirthMetric("Temperature", PlugBDataType.Double, 22.1);

// 3. Start: connect -> set NDEATH as LWT -> send NBIRTH -> send DBIRTH for plc1
await client.StartAsync();

// 4. Publish runtime data (DDATA + seq numbers handled for you)
var metric = MetricBuilder.Create("Temperature").WithValue(25.4).Build();
await plc1.PublishDataAsync(metric);

// 5. Graceful shutdown
await client.DisposeAsync();
```

### High availability: Primary Host, failover & store-and-forward

```csharp
IPlugBClient client = new PlugBClientBuilder()
    .WithServers(
        new MqttServer("primary.mqtt.local", 1883),
        new MqttServer("backup.mqtt.local", 1883))
    .WithNodeId("Factory_01", "EdgeGateway_A")
    .WithPrimaryHost("SCADA_1")                 // hold NBIRTH until the host is online
    .WithStoreAndForward(o =>                    // bounded, explicit buffering
    {
        o.Capacity = 100_000;
        o.Eviction = EvictionPolicy.DropOldest;
        o.Store = new FileForwardStore("./plugb-buffer"); // or InMemoryForwardStore (default)
    })
    .Build();
```

## Key Features

- **Spec-correct lifecycle, automatically** — `NBIRTH`, `NDEATH` (as MQTT Last Will) and
  `DBIRTH` are driven by the connection state, with a matching `bdSeq` across `NDEATH` and
  `NBIRTH`.
- **Encapsulated sequence management** — `seq` (0–255 wrap-around) is handled for you:
  `NBIRTH = 0`, every following message increments, and `NDEATH` correctly carries no `seq`.
- **Thread-safe publishing** — all publishing runs through a single serialized pipeline per
  Edge Node, so the shared `seq` stays monotonic under concurrent calls.
- **Primary Host aware** — holds `NBIRTH`/`DBIRTH` until the configured host's `STATE` shows it
  online (with stale-timestamp rejection), per Sparkplug 3.0.
- **Fault-tolerant failover** — configurable broker list; fails over to the next server where
  the Primary Host is online.
- **Store-and-forward, done right** — explicit, bounded buffering (in-memory or file-backed);
  data is sent only *after* a fresh re-birth, with new `seq` numbers and flagged
  `is_historical`, never a hidden queue replaying stale messages.
- **Rebirth handled out of the box** — responds to `Node Control/Rebirth` commands
  automatically.
- **Full data type support** — Int8/16/32/64, UInt8/16/32/64, Float, Double, Boolean, String,
  DateTime, Text, plus the optional DataSet, Bytes, File and Template types.
- **Clean & testable** — no QoS, retained flags or Protobuf in the public API;
  `IPlugBClient` / `IPlugBDevice` are mockable for broker-free unit tests.

## Scope

PlugB is, by design, an **Edge Node publisher SDK**. It can be *aware* of a Primary Host
(consuming its `STATE` for birth-gating and failover), but it does **not** act as a Host /
Primary Application, does **not** publish its own `STATE`, and does **not** decode `BIRTH`/`DATA`
messages from other nodes. Beyond its own `NCMD`/`DCMD` commands and the configured Primary
Host's `STATE`, it does not interpret foreign traffic.

## Requirements

- **.NET 10** (`net10.0`) or later
- A Sparkplug-B-capable MQTT broker (Mosquitto, EMQX, HiveMQ, …)

## Documentation & Source

Full documentation, the architecture overview, and a runnable sample are on GitHub:
**[github.com/philipp2604/PlugB](https://github.com/philipp2604/PlugB)**

## License

Licensed under the **Apache License 2.0**. Bundled third-party components and their licenses
are documented in
[THIRD-PARTY-NOTICES.txt](https://github.com/philipp2604/PlugB/blob/master/THIRD-PARTY-NOTICES.txt).