# PlugB 💥🔌

[![Language](https://img.shields.io/badge/language-C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
![Target Framework](https://img.shields.io/badge/.NET-10.0-purple.svg)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET 10 Build & Test](https://github.com/philipp2604/PlugB/actions/workflows/dotnet-10-build-and-test.yml/badge.svg)](https://github.com/philipp2604/PlugB/actions/workflows/dotnet-10-build-and-test.yml)
[![GitHub issues](https://img.shields.io/github/issues/philipp2604/PlugB)](https://github.com/philipp2604/PlugB/issues)
[![NuGet Version](https://img.shields.io/nuget/v/philipp2604.PlugB.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/philipp2604.PlugB/)

**PlugB** is a clean, type-safe C# library that lets you publish industrial data as a
**Sparkplug B Edge Node** — without ever touching MQTT topics, QoS levels, retained flags,
Protobuf byte arrays, or sequence numbers.

It wraps [MQTTnet](https://github.com/dotnet/MQTTnet) and the Eclipse Tahu Sparkplug B
payload schema behind a small fluent API and handles the parts that hand-rolled Sparkplug
wrappers almost always get wrong: the **NBIRTH/NDEATH/DBIRTH lifecycle**, the **`seq`/`bdSeq`
sequence management**, and **thread-safe ordered publishing**. Beyond the core, it can wait for
a **Primary Host**, **fail over** across multiple brokers, and **buffer data** while offline.

> **⚠️ Work In Progress (WIP)**
>
> PlugB is in active development. The Edge Node feature set (lifecycle, sequence management,
> metrics, NCMD/Rebirth handling, Primary Host STATE gating, multi-server failover, and
> store-and-forward) is implemented and covered by tests, but the API surface may still change
> before the first stable release. Review the **Scope & Limitations** section before using it
> in production.

## 🚀 Why PlugB?

- **Spec-correct lifecycle, automatically.** `NBIRTH`, `NDEATH` and `DBIRTH` are driven by the
  connection state, not by manual method calls. The `NDEATH` is registered as the MQTT Last
  Will & Testament inside the `CONNECT` packet, and shares its `bdSeq` with the matching
  `NBIRTH`.
- **Encapsulated sequence management.** The `seq` counter (0–255, wrap-around) is handled for
  you: `NBIRTH` is always `0`, every subsequent message increments, and `NDEATH` correctly
  carries no `seq`. You cannot accidentally emit a spec-violating sequence.
- **Thread-safe by design.** All publishing goes through a single serialized pipeline per Edge
  Node (a `System.Threading.Channels` consumer loop). Even under concurrent calls from many
  devices and threads, the shared `seq` stays monotonic — no gaps, no duplicates.
- **Primary Host aware.** Configure a Primary Host and PlugB holds `NBIRTH`/`DBIRTH` until that
  host's `STATE` shows it online (with stale-timestamp rejection), buffering data until then —
  exactly as the Sparkplug 3.0 spec requires.
- **Fault-tolerant failover.** Give it a list of brokers; when the Primary Host isn't reachable
  on the current server, PlugB walks to the next one where it is.
- **Store-and-forward, done right.** Buffering is explicit and opt-in — never a hidden queue
  that replays stale messages. Data captured while the host or connection is down is sent only
  *after* a fresh `NBIRTH`, with new `seq` numbers and flagged `is_historical`, so it can never
  corrupt state alignment on the host.
- **Rebirth handled out of the box.** An incoming `NCMD` with `Node Control/Rebirth = true`
  triggers a fresh `NBIRTH` plus all `DBIRTH`s — no work required from you.
- **Clean Developer Experience.** No QoS, no retained flags, no Protobuf in the public API.
  Just a fluent builder, devices, and `PublishDataAsync`.
- **Testable.** `IPlugBClient` and `IPlugBDevice` are interfaces, and the transport is
  abstracted, so the entire lifecycle, sequence and gating logic can be unit-tested **without a
  real broker**.

## ⚡ Quickstart

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

### High availability: Primary Host, failover & store-and-forward

```csharp
using PlugB.Abstractions;
using PlugB.Builders;
using PlugB.Options;
using PlugB.Storage;

IPlugBClient client = new PlugBClientBuilder()
    // Multiple brokers — PlugB fails over to the next one when the
    // Primary Host isn't reachable on the current server.
    .WithServers(
        new MqttServer("primary.mqtt.local", 1883),
        new MqttServer("backup.mqtt.local", 1883))
    .WithNodeId("Factory_01", "EdgeGateway_A")
    // Hold NBIRTH/DBIRTH until this host's STATE shows it online,
    // and buffer data until then, Timeout = 30s.
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

## ✨ Implemented Features

### 🔄 Lifecycle & Connectivity
- [x] **Automatic Birth/Death**: `NBIRTH`, `NDEATH` (as LWT), `DBIRTH` driven by connection state.
- [x] **bdSeq coupling**: matching `bdSeq` across `NDEATH` and `NBIRTH`, incremented per connect.
- [x] **Self-healing reconnect**: own backoff logic, fresh re-birth of node and all devices.
- [x] **Rebirth**: responds to `Node Control/Rebirth` commands automatically.
- [x] **Command subscriptions**: subscribes to `NCMD` / `DCMD` on connect.

### 🛰 Primary Host & Resilience
- [x] **Primary Host STATE gating**: subscribes to `spBv1.0/STATE/{hostId}`, parses the JSON
      `{online, timestamp}` payload, and holds `NBIRTH`/`DBIRTH` until the host is online —
      including stale-timestamp rejection per the spec.
- [x] **Multi-server failover**: configurable broker list; fails over to the next server where
      the Primary Host is online.
- [x] **Store-and-Forward**: bounded buffer with configurable eviction
      (`DropOldest` / `DropNewest` / `RejectNew`); **in-memory** and **file-backed**
      (restart-durable) stores.
- [x] **Historical backfill**: buffered data is replayed after re-birth with
      `is_historical = true` and original timestamps, using fresh `seq` numbers.
- [x] **Sparkplug 3.0 conformance**: STATE handling, timestamp rules and failover are verified
      against the specification.

### 📊 Metrics & Data Types
- [x] **Sequence management**: `seq` (0–255 wrap-around), `NBIRTH = 0`, `NDEATH` without `seq`.
- [x] **Full data type support**: Int8/16/32/64, UInt8/16/32/64, Float, Double, Boolean,
      String, DateTime, Text — plus the optional **DataSet**, **Bytes**, **File** and
      **Template** types.
- [x] **Metric properties**: typed property sets, including the well-known `is_historical`.
- [x] **Correct Protobuf encoding**: including the unsigned-int-in-`long_value` semantics.
- [x] **Aliases**: optional per-metric aliases (name in BIRTH, alias in DATA).
- [x] **Timestamps**: payload-level and per-metric epoch-millis (UTC).

### 🛠 Architecture & Quality
- [x] **Fluent API**: `PlugBClientBuilder`, `MetricBuilder`, `record`-based options.
- [x] **Serialized publish pipeline**: one ordered consumer per Edge Node (thread-safe `seq`),
      with the store-and-forward gate on the same path.
- [x] **Mockable interfaces**: `IPlugBClient` / `IPlugBDevice`, transport abstracted for tests.
- [x] **Async/await**: fully asynchronous, `CancellationToken` support throughout.

## 🚧 Scope & Limitations

PlugB is — by design — an **Edge Node publisher SDK**. It can be *aware* of a Primary Host
(consuming its `STATE` for birth-gating and failover), but it is not itself a host. The
following are intentionally **out of scope** for the current version:

- ❌ **Host / Primary Application role.** PlugB consumes a Primary Host's `STATE`, but it does
  not act as a SCADA/MES host application and does not publish its own `STATE`.
- ❌ **Consuming / decoding foreign messages.** Beyond its own `NCMD`/`DCMD` commands and the
  configured Primary Host's `STATE` topic, PlugB does not subscribe to, decode, or interpret
  `BIRTH`/`DATA` payloads from other Edge Nodes or devices.

### Roadmap
- [ ] Sparkplug Host Application / consumer layer (decoding foreign BIRTH/DATA).

## 📦 Architecture

The library is split into four logical layers, hiding everything below the public API:

1. **Public API & DX**: `IPlugBClient`, `IPlugBDevice`, `PlugBClientBuilder`, `MetricBuilder`,
   `PlugBOptions`, `PlugBDataType`, plus the resilience options (`MqttServer`,
   `StoreAndForwardOptions`, `EvictionPolicy`, `IForwardStore`).
2. **Domain & Mapping**: `PayloadBuilder`, `TopicGenerator`, `DataTypeConverter`, `StateParser`
   — translating the friendly C# world into the strict Sparkplug B specification.
3. **State & Sequence** *(internal)*: `SequenceManager` (`seq` + `bdSeq`), `DeviceRegistry`,
   `PrimaryHostMonitor`, and the forward stores (`InMemoryForwardStore`, `FileForwardStore`).
4. **Transport & Lifecycle** *(internal)*: `MqttTransport` over MQTTnet, the
   `ConnectionStateMachine` (combined connection + host state), `ServerSelector` (failover),
   connect/LWT logic, and the serialized publish pipeline with its store-and-forward gate.

The generated Sparkplug B Protobuf types are kept strictly `internal` and never leak into the
public API.

## 🧪 Building & Testing

```bash
dotnet build
dotnet test
```

- **Unit tests** run broker-free: the transport is abstracted, so lifecycle, sequence, topic,
  payload, STATE, gating and store-and-forward logic are verified by inspecting the messages
  PlugB would emit.
- **Integration tests** spin up a real Mosquitto broker via
  [Testcontainers](https://dotnet.testcontainers.org/) (`Testcontainers.Mosquitto`) and
  therefore **require a running Docker engine**. They cover STATE gating, multi-server failover
  and historical flush, and are skipped automatically when Docker is unavailable, so the
  unit-test run stays green.

## ▶️ Running the Sample

A runnable console demo lives in [`src/PlugB.Sample`](./src/PlugB.Sample). Its `Broker/`
folder ships a `docker-compose.yml` that starts a single, pre-configured Mosquitto broker —
the sample application itself is started separately. See the
[sample README](./src/PlugB.Sample/README.md) for details:

```bash
# 1. start the broker
docker compose -f src/PlugB.Sample/Broker/docker-compose.yml up -d
# 2. run the Edge Node sample, it will start buffering messages while the host is offline
dotnet run --project src/PlugB.Sample
# 3. run the Host sample, use the 'o' and 'f' keys to switch the host's online mode
dotnet run --project src/PlugB.Sample --no-build "--host"
```

## ⚙️ Requirements

- **.NET 10 SDK** or later (`net10.0`).
- A Sparkplug-B-capable MQTT broker (Mosquitto, EMQX, HiveMQ, …).
- **Docker** — only for the integration tests and the sample broker.

## 📚 Dependencies & Licenses

PlugB intentionally builds on the battle-tested **MQTTnet** client rather than
reimplementing the MQTT protocol, and uses **Google.Protobuf** with the Eclipse Tahu
`sparkplug_b.proto` schema for payload encoding. The `STATE` payload is parsed as JSON via the
BCL (`System.Text.Json`), and the file-backed store uses only BCL I/O — no extra runtime
dependencies.

A complete list of bundled third-party components and their licenses is documented in:

📄 **[THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt)**

## 🤝 Contributing

Contributions are welcome! Please keep the public API free of MQTT/Protobuf types, add a unit
test for any change touching the lifecycle, sequence or gating logic, and make sure
`dotnet build` stays warning-free (warnings are treated as errors).

## ⚖️ License

This project is licensed under the **Apache License 2.0**. See the [LICENSE](./LICENSE.txt)
file for details. You are free to use, modify, and distribute this software in **commercial**
and **private** applications.

---
*Built for clean IT/OT connectivity — so you can publish Sparkplug B without reading the
260-page spec first.*