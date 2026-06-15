# PlugB 💥🔌

**PlugB** is a clean, type-safe C# library for **Sparkplug B** on .NET. It lets you **publish**
industrial data as an **Edge Node** and **consume** it as a **Host Application** — without ever
touching MQTT topics, QoS levels, retained flags, Protobuf byte arrays, or sequence numbers.

It wraps [MQTTnet](https://github.com/dotnet/MQTTnet) and the Eclipse Tahu Sparkplug B payload
schema behind a small fluent API and handles the parts that hand-rolled Sparkplug wrappers
almost always get wrong: the **NBIRTH/NDEATH/DBIRTH lifecycle**, the **`seq`/`bdSeq` sequence
management**, **thread-safe ordered publishing**, Primary-Host **STATE gating**, multi-broker
**failover**, and — on the consumer side — **payload decoding**, **alias resolution**, and
automatic **rebirth** requests when a sequence gap is detected.

> **⚠️ Work In Progress (WIP)**
>
> PlugB is in active development. Both the **Edge Node** SDK (lifecycle, sequence management,
> metrics, NCMD/Rebirth handling, Primary Host STATE gating, multi-server failover,
> store-and-forward) and the **Host Application** (decoding foreign BIRTH/DATA, namespace
> model, alias resolution, auto-rebirth, STATE publishing, commands) are implemented and covered
> by unit and integration tests — but the API surface may still change before the first stable
> release. Review the **Scope & Limitations** section before using it in production.

## Installation

```bash
dotnet add package philipp2604.PlugB
```

## Quickstart — Edge Node (publish)

```bash
dotnet add package philipp2604.PlugB
```

```csharp
using PlugB.Abstractions;
using PlugB.Builders;
using PlugB.Models;

// 1. Configure the Edge Node via the fluent builder
IPlugBClient client = new PlugBClientBuilder()
    .WithBroker("127.0.0.1", 1883)
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

### Edge Node — high availability: Primary Host, failover & store-and-forward

```csharp
using PlugB.Abstractions;
using PlugB.Builders;
using PlugB.Options;
using PlugB.Storage;

IPlugBClient client = new PlugBClientBuilder()
    // Multiple brokers — PlugB fails over to the next one when the
    // Primary Host isn't reachable on the current server.
    .WithBrokers(
        new MqttBroker("primary.mqtt.local", 1883),
        new MqttBroker("backup.mqtt.local", 1883))
    .WithNodeId("Factory_01", "EdgeGateway_A")
    // Hold NBIRTH/DBIRTH until this host's STATE shows it online,
    // and buffer data until then (failover timeout = 30s).
    .WithPrimaryHost("SCADA_1", TimeSpan.FromSeconds(30))
    // Explicit, bounded store-and-forward (defaults shown).
    .WithStoreAndForward(o =>
    {
        o.Capacity = 100_000;
        o.Eviction = EvictionPolicy.DropOldest;
        o.Store = new FileForwardStore("./plugb-buffer", 100_000, EvictionPolicy.DropOldest); // or InMemoryForwardStore (default)
    })
    .Build();

await client.StartAsync();
// While SCADA_1 is offline, PublishDataAsync buffers. Once it comes back online,
// PlugB re-births and flushes the buffered data as historical (is_historical = true).
```

## Quickstart — Host Application (consume)

```csharp
using PlugB.Abstractions;
using PlugB.Builders;
using PlugB.Models;

// 1. Configure the Host via the fluent builder
await using IPlugBHost host = new PlugBHostBuilder()
    .WithBroker("127.0.0.1", 1883)
    .WithHostId("SCADA_1")              // required: identifies the Host App and its STATE topic
    .WithGroupFilter("Factory_01")      // optional, repeatable; default: all groups
    .Build();

// 2. Subscribe to decoded, typed events
host.NodeBirth   += (_, e) => Console.WriteLine($"NBIRTH {e.GroupId}/{e.EdgeNodeId}: {string.Join(", ", e.Metrics.Keys)}");
host.DeviceBirth += (_, e) => Console.WriteLine($"DBIRTH {e.EdgeNodeId}/{e.DeviceId}");
host.DataChanged += (_, e) =>
{
    // e.DeviceId == null  =>  node-level data
    foreach (var m in e.Metrics)
        Console.WriteLine($"{e.EdgeNodeId}/{e.DeviceId}: {m.Name} = {m.Value}");
};
host.NodeDeath += (_, e) => Console.WriteLine($"NDEATH {e.EdgeNodeId}");

// 3. Start: connect -> publish STATE (online) -> subscribe and start decoding
await host.StartAsync();

// 4. Inspect the live namespace model at any time
IReadOnlyCollection<HostNode> nodes = host.Nodes;   // HostNode -> HostDevice -> current Metrics

// 5. Send commands / request a rebirth
await host.RequestRebirthAsync("Factory_01", "EdgeGateway_A");
var setpoint = MetricBuilder.Create("Setpoint").WithValue(80.0).Build();
await host.SendDeviceCommandAsync("Factory_01", "EdgeGateway_A", "PLC_Machine_1", [setpoint]);
```

## Implemented Features

### Edge Node — Lifecycle & Connectivity
- [x] **Automatic Birth/Death**: `NBIRTH`, `NDEATH` (as LWT), `DBIRTH` driven by connection state.
- [x] **bdSeq coupling**: matching `bdSeq` across `NDEATH` and `NBIRTH`, incremented per connect.
- [x] **Self-healing reconnect**: own backoff logic, fresh re-birth of node and all devices.
- [x] **Rebirth**: responds to `Node Control/Rebirth` commands automatically.
- [x] **Command subscriptions**: subscribes to `NCMD` / `DCMD` on connect.

### Edge Node — Primary Host & Resilience
- [x] **Primary Host STATE gating**: subscribes to `spBv1.0/STATE/{hostId}`, parses the JSON
      `{online, timestamp}` payload, and holds `NBIRTH`/`DBIRTH` until the host is online —
      including stale-timestamp rejection per the spec.
- [x] **Multi-server failover**: configurable broker list; fails over to the next server.
- [x] **Store-and-Forward**: bounded buffer with configurable eviction
      (`DropOldest` / `DropNewest` / `RejectNew`); **in-memory** and **file-backed**
      (restart-durable) stores.
- [x] **Historical backfill**: buffered data is replayed after re-birth with
      `is_historical = true` and original timestamps, using fresh `seq` numbers.

### Host Application / Consumer
- [x] **Foreign payload decoding**: decodes `NBIRTH`, `DBIRTH`, `NDATA`, `DDATA`, `NDEATH`,
      `DDEATH` into typed `Metric`s (full data-type coverage, incl. DataSet/Template/PropertySet).
- [x] **Live namespace model**: `host.Nodes` exposes `HostNode` → `HostDevice` → current metrics.
- [x] **Alias resolution**: BIRTH binds name↔alias; DATA carrying only aliases is resolved back
      to names.
- [x] **Sequence tracking & auto-rebirth**: detects `seq` gaps / data-before-birth and requests a
      `Node Control/Rebirth` automatically (toggle via `WithRebirthOnGap`).
- [x] **bdSeq correlation**: a stale `NDEATH` with a mismatched `bdSeq` is ignored.
- [x] **Primary Host STATE publishing**: birth/will share one timestamp, retained at QoS 1, so
      Edge Nodes can gate on this host.
- [x] **Commands**: `RequestRebirthAsync`, `SendNodeCommandAsync`, `SendDeviceCommandAsync`.
- [x] **Typed events**: `NodeBirth`, `DeviceBirth`, `DataChanged`, `NodeDeath`, `DeviceDeath`,
      `RebirthRequested`, `DecodeFailed`, `ConnectionChanged`.

### Metrics & Data Types
- [x] **Sequence management**: `seq` (0–255 wrap-around), `NBIRTH = 0`, `NDEATH` without `seq`.
- [x] **Full data type support**: Int8/16/32/64, UInt8/16/32/64, Float, Double, Boolean,
      String, DateTime, Text — plus the optional **DataSet**, **Bytes**, **File** and
      **Template** types.
- [x] **Metric properties**: typed property sets, including the well-known `is_historical`.
- [x] **Correct Protobuf round-trip**: including the unsigned-int-in-`long_value` semantics.
- [x] **Aliases**: optional per-metric aliases (name in BIRTH, alias in DATA).
- [x] **Timestamps**: payload-level and per-metric epoch-millis (UTC).

### Architecture & Quality
- [x] **Fluent API**: `PlugBClientBuilder`, `PlugBHostBuilder`, `MetricBuilder`, `record`-based options.
- [x] **Serialized publish pipeline**: one ordered consumer per Edge Node (thread-safe `seq`),
      with the store-and-forward gate on the same path.
- [x] **Mockable interfaces**: `IPlugBClient`, `IPlugBDevice`, `IPlugBHost`; transport abstracted.
- [x] **Async/await**: fully asynchronous, `CancellationToken` support throughout.
- [x] **Sparkplug 3.0 conformance**: lifecycle, sequencing, STATE handling, timestamp rules,
      failover and decoding are verified against the specification by the test suite.

## Scope & Limitations

PlugB covers two complementary Sparkplug B roles: an **Edge Node publisher SDK** and a **Host
Application / consumer**. The following are intentionally **out of scope** for the current version:

- ❌ **Persistent historian / HDA.** The Host surfaces live data through events and the
  `host.Nodes` snapshot; long-term historical storage is left to your application.
- ❌ **Template / UDT schema validation.** Templates are decoded faithfully, but not validated
  against their definitions.
- ❌ **Multi-host election / coordination.** A single configured Host ID acts as a Primary Host;
  PlugB does not coordinate failover between competing Host Applications.

## Architecture

PlugB ships two cooperating clients — `IPlugBClient` (Edge Node) and `IPlugBHost` (Host
Application) — that share the same internal mapping and transport building blocks. Everything
below the public API is `internal`, and the generated Sparkplug B Protobuf types never leak out.

1. **Public API & DX**: `IPlugBClient`, `IPlugBDevice`, `IPlugBHost`, `PlugBClientBuilder`,
   `PlugBHostBuilder`, `MetricBuilder`; options (`PlugBOptions`, `PlugBHostOptions`, `MqttBroker`,
   `StoreAndForwardOptions`, `EvictionPolicy`, `IForwardStore`); models (`Metric`, `HostNode`,
   `HostDevice`, `PlugBDataType`, …) and the host event args.
2. **Domain & Mapping** *(internal)*: `PayloadBuilder` / `PayloadDecoder`, `TopicGenerator` /
   `TopicParser`, `DataTypeConverter` (encode **and** decode), `StateParser` / `StateSerializer`.
3. **State & Sequence** *(internal)*: `SequenceManager` (`seq` + `bdSeq`), `DeviceRegistry`,
   `PrimaryHostMonitor`, the forward stores (`InMemoryForwardStore`, `FileForwardStore`), and on
   the consumer side `HostNodeRegistry` (namespace + alias maps) and `HostSequenceTracker`.
4. **Transport & Lifecycle** *(internal)*: `MqttTransport` (Edge) and `HostMqttTransport` (Host)
   over MQTTnet, the `ConnectionStateMachine`, `ServerSelector` (failover), connect/LWT/STATE
   logic, and the serialized publish pipeline with its store-and-forward gate.

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