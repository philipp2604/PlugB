# PlugB 💥🔌

[![Language](https://img.shields.io/badge/language-C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
![Target Framework](https://img.shields.io/badge/.NET-10.0-purple.svg)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET 10 Build & Test](https://github.com/philipp2604/PlugB/actions/workflows/dotnet-10-build-and-test.yml/badge.svg)](https://github.com/philipp2604/PlugB/actions/workflows/dotnet-10-build-and-test.yml)
[![GitHub issues](https://img.shields.io/github/issues/philipp2604/PlugB)](https://github.com/philipp2604/PlugB/issues)
[![NuGet Version](https://img.shields.io/nuget/v/philipp2604.PlugB.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/philipp2604.PlugB/)

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

## 🚀 Why PlugB?

- **Publisher *and* consumer in one library.** Use `IPlugBClient` to act as an Edge Node, and
  `IPlugBHost` to act as a Sparkplug Host Application — both behind the same clean, fluent API.
- **Spec-correct lifecycle, automatically.** `NBIRTH`, `NDEATH` and `DBIRTH` are driven by the
  connection state, not by manual method calls. The `NDEATH` is registered as the MQTT Last Will
  & Testament inside the `CONNECT` packet, and shares its `bdSeq` with the matching `NBIRTH`.
- **Encapsulated sequence management.** The `seq` counter (0–255, wrap-around) is handled for
  you: `NBIRTH` is always `0`, every subsequent message increments, and `NDEATH` correctly
  carries no `seq`. You cannot accidentally emit a spec-violating sequence.
- **Thread-safe by design.** All publishing goes through a single serialized pipeline per Edge
  Node (a `System.Threading.Channels` consumer loop). Even under concurrent calls from many
  devices and threads, the shared `seq` stays monotonic — no gaps, no duplicates.
- **Primary Host aware.** Configure a Primary Host and the Edge Node holds `NBIRTH`/`DBIRTH`
  until that host's `STATE` shows it online (with stale-timestamp rejection), buffering data
  until then — exactly as the Sparkplug 3.0 spec requires.
- **Fault-tolerant failover.** Give it a list of brokers; when the Primary Host isn't reachable
  on the current server, PlugB walks to the next one where it is.
- **Store-and-forward, done right.** Buffering is explicit and opt-in — never a hidden queue
  that replays stale messages. Data captured while the host or connection is down is sent only
  *after* a fresh `NBIRTH`, with new `seq` numbers and flagged `is_historical`.
- **A real consumer, not just a decoder.** The Host decodes every BIRTH/DATA into typed metrics,
  resolves aliases, maintains a live namespace model, and automatically requests a rebirth when
  it detects a sequence gap or data before birth.
- **Clean Developer Experience.** No QoS, no retained flags, no Protobuf in the public API.
- **Testable.** The public surface is interface-based and the transport is abstracted, so the
  entire lifecycle, sequence, gating and decoding logic can be unit-tested **without a real broker**.

## ⚡ Quickstart — Edge Node (publish)

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

## ⚡ Quickstart — Host Application (consume)

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

## ✨ Implemented Features

### 🔄 Edge Node — Lifecycle & Connectivity
- [x] **Automatic Birth/Death**: `NBIRTH`, `NDEATH` (as LWT), `DBIRTH` driven by connection state.
- [x] **bdSeq coupling**: matching `bdSeq` across `NDEATH` and `NBIRTH`, incremented per connect.
- [x] **Self-healing reconnect**: own backoff logic, fresh re-birth of node and all devices.
- [x] **Rebirth**: responds to `Node Control/Rebirth` commands automatically.
- [x] **Command subscriptions**: subscribes to `NCMD` / `DCMD` on connect.

### 🛰 Edge Node — Primary Host & Resilience
- [x] **Primary Host STATE gating**: subscribes to `spBv1.0/STATE/{hostId}`, parses the JSON
      `{online, timestamp}` payload, and holds `NBIRTH`/`DBIRTH` until the host is online —
      including stale-timestamp rejection per the spec.
- [x] **Multi-server failover**: configurable broker list; fails over to the next server.
- [x] **Store-and-Forward**: bounded buffer with configurable eviction
      (`DropOldest` / `DropNewest` / `RejectNew`); **in-memory** and **file-backed**
      (restart-durable) stores.
- [x] **Historical backfill**: buffered data is replayed after re-birth with
      `is_historical = true` and original timestamps, using fresh `seq` numbers.

### 📥 Host Application / Consumer
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

### 📊 Metrics & Data Types
- [x] **Sequence management**: `seq` (0–255 wrap-around), `NBIRTH = 0`, `NDEATH` without `seq`.
- [x] **Full data type support**: Int8/16/32/64, UInt8/16/32/64, Float, Double, Boolean,
      String, DateTime, Text — plus the optional **DataSet**, **Bytes**, **File** and
      **Template** types.
- [x] **Metric properties**: typed property sets, including the well-known `is_historical`.
- [x] **Correct Protobuf round-trip**: including the unsigned-int-in-`long_value` semantics.
- [x] **Aliases**: optional per-metric aliases (name in BIRTH, alias in DATA).
- [x] **Timestamps**: payload-level and per-metric epoch-millis (UTC).

### 🛠 Architecture & Quality
- [x] **Fluent API**: `PlugBClientBuilder`, `PlugBHostBuilder`, `MetricBuilder`, `record`-based options.
- [x] **Serialized publish pipeline**: one ordered consumer per Edge Node (thread-safe `seq`),
      with the store-and-forward gate on the same path.
- [x] **Mockable interfaces**: `IPlugBClient`, `IPlugBDevice`, `IPlugBHost`; transport abstracted.
- [x] **Async/await**: fully asynchronous, `CancellationToken` support throughout.
- [x] **Sparkplug 3.0 conformance**: lifecycle, sequencing, STATE handling, timestamp rules,
      failover and decoding are verified against the specification by the test suite.

## 🚧 Scope & Limitations

PlugB covers two complementary Sparkplug B roles: an **Edge Node publisher SDK** and a **Host
Application / consumer**. The following are intentionally **out of scope** for the current version:

- ❌ **Persistent historian / HDA.** The Host surfaces live data through events and the
  `host.Nodes` snapshot; long-term historical storage is left to your application.
- ❌ **Template / UDT schema validation.** Templates are decoded faithfully, but not validated
  against their definitions.
- ❌ **Multi-host election / coordination.** A single configured Host ID acts as a Primary Host;
  PlugB does not coordinate failover between competing Host Applications.

## 📦 Architecture

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

## 🧪 Building & Testing

```bash
dotnet build
dotnet test
```

- **Unit tests** run broker-free: lifecycle, sequencing, topic generation/parsing, payload
  encode/decode round-trips, STATE handling, gating, store-and-forward, host registry and
  sequence-tracker logic are all verified without a network.
- **Integration tests** spin up a real Mosquitto broker via
  [Testcontainers](https://dotnet.testcontainers.org/) (`Testcontainers.Mosquitto`) and therefore
  **require a running Docker engine**. They cover STATE gating, multi-server failover, historical
  flush, and the end-to-end Edge↔Host round-trip (decoding, alias resolution and auto-rebirth).
  They are skipped automatically when Docker is unavailable, so the unit-test run stays green.

## ▶️ Running the Sample

The sample demonstrates the full round-trip as **two processes against one broker**: an Edge Node
that gates on its Primary Host and buffers data while the host is offline, and a Host Application
that comes online, decodes the incoming data, and can drive a rebirth.

```bash
# 1. Start the broker
docker compose -f src/PlugB.Sample/Broker/docker-compose.yml up -d

# 2. Terminal 1 — start the Edge Node.
#    The Primary Host (SCADA_1) is still offline, so it connects and buffers a Counter
#    metric to the on-disk file store every 2 seconds (store-and-forward).
dotnet run --project src/PlugB.Sample

# 3. Terminal 2 — start the Host application.
#    It publishes STATE online; the waiting Edge Node detects the transition, flushes its
#    buffered data as historical, then publishes NBIRTH/DBIRTH and goes live with DDATA.
dotnet run --project src/PlugB.Sample --no-build "--host"
```

In the **Host** terminal:

- **`R`** — send a **Rebirth Request**. Watch the Edge Node react, run a full rebirth cycle, and
  send fresh BIRTH certificates.
- **`S`** — print the Host's current **namespace snapshot** (everything it has decoded so far).
- **`Q`** — quit (publishes a graceful OFFLINE `STATE` first).

The Edge Node terminal logs its state machine, buffering and live publishing as it goes. See the
[sample README](./src/PlugB.Sample/README.md) for more details.

## ⚙️ Requirements

- **.NET 10 SDK** or later (`net10.0`).
- A Sparkplug-B-capable MQTT broker (Mosquitto, EMQX, HiveMQ, …).
- **Docker** — only for the integration tests and the sample broker.

## 📚 Dependencies & Licenses

PlugB intentionally builds on the battle-tested **MQTTnet** client rather than reimplementing the
MQTT protocol, and uses **Google.Protobuf** with the Eclipse Tahu `sparkplug_b.proto` schema for
payload encoding and decoding. The `STATE` payload is handled as JSON via the BCL
(`System.Text.Json`), and the file-backed store uses only BCL I/O — no extra runtime dependencies.

A complete list of bundled third-party components and their licenses is documented in:

📄 **[THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt)**

## 🤝 Contributing

Contributions are welcome! Please keep the public API free of MQTT/Protobuf types, add a unit
test for any change touching the lifecycle, sequence, gating or decoding logic, and make sure
`dotnet build` stays warning-free (warnings are treated as errors).

## ⚖️ License

This project is licensed under the **Apache License 2.0**. See the [LICENSE](./LICENSE.txt)
file for details. You are free to use, modify, and distribute this software in **commercial**
and **private** applications.

---
*Built for clean IT/OT connectivity — so you can speak Sparkplug B, in both directions, without
reading the 260-page spec first.*