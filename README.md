# PlugB 🔌

[![Language](https://img.shields.io/badge/language-C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
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
sequence management**, and **thread-safe ordered publishing**.

> **⚠️ Work In Progress (WIP)**
>
> PlugB is in active development. The Edge Node core (lifecycle, sequence management,
> metrics, NCMD/Rebirth handling) is implemented and covered by tests, but the API surface
> may still change before the first stable release. Review the **Scope & Limitations**
> section before using it in production.

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
- **No hidden offline queue.** This is deliberate. After a reconnect, Sparkplug requires a
  fresh `NBIRTH` before any data — replaying stale, previously-queued messages would corrupt
  state alignment on the host. PlugB reconnects cleanly and re-births instead of dumping a
  backlog.
- **Rebirth handled out of the box.** An incoming `NCMD` with `Node Control/Rebirth = true`
  triggers a fresh `NBIRTH` plus all `DBIRTH`s — no work required from you.
- **Clean Developer Experience.** No QoS, no retained flags, no Protobuf in the public API.
  Just a fluent builder, devices, and `PublishDataAsync`.
- **Testable.** `IPlugBClient` and `IPlugBDevice` are interfaces, and the transport is
  abstracted, so the entire lifecycle and sequence logic can be unit-tested **without a real
  broker**.

## ⚡ Quickstart

```bash
dotnet add package philipp2604.PlugB
```

```csharp
using PlugB;

// 1. Configure the Edge Node via the fluent builder
IPlugBClient client = new PlugBClientBuilder()
    .WithBroker("mqtt.server.local", 1883)
    .WithNodeId("Factory_01", "EdgeGateway_A")
    // optional: .WithPrimaryHost("SCADA_1")
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

## ✨ Implemented Features

### 🔄 Lifecycle & Connectivity
- [x] **Automatic Birth/Death**: `NBIRTH`, `NDEATH` (as LWT), `DBIRTH` driven by connection state.
- [x] **bdSeq coupling**: matching `bdSeq` across `NDEATH` and `NBIRTH`, incremented per connect.
- [x] **Self-healing reconnect**: own backoff logic, fresh re-birth of node and all devices.
- [x] **Rebirth**: responds to `Node Control/Rebirth` commands automatically.
- [x] **Command subscriptions**: subscribes to `NCMD` / `DCMD` on connect.

### 📊 Metrics & Data Types
- [x] **Sequence management**: `seq` (0–255 wrap-around), `NBIRTH = 0`, `NDEATH` without `seq`.
- [x] **Full data type support**: Int8/16/32/64, UInt8/16/32/64, Float, Double, Boolean,
      String, DateTime, Text — plus the optional **DataSet**, **Bytes**, **File** and
      **Template** types.
- [x] **Correct Protobuf encoding**: including the unsigned-int-in-`long_value` semantics.
- [x] **Aliases**: optional per-metric aliases (name in BIRTH, alias in DATA).
- [x] **Timestamps**: payload-level and per-metric epoch-millis (UTC).

### 🛠 Architecture & Quality
- [x] **Fluent API**: `PlugBClientBuilder`, `MetricBuilder`, `record`-based options.
- [x] **Serialized publish pipeline**: one ordered consumer per Edge Node (thread-safe `seq`).
- [x] **Mockable interfaces**: `IPlugBClient` / `IPlugBDevice`, transport abstracted for tests.
- [x] **Async/await**: fully asynchronous, `CancellationToken` support throughout.

## 🚧 Scope & Limitations

PlugB is — by design — an **Edge Node publisher SDK**. The following are intentionally **out
of scope** for the current version:

- ❌ **Host / Primary Application role.** PlugB connects as an Edge Node; it does not act as a
  SCADA/MES host application.
- ❌ **`STATE` topic.** PlugB neither publishes nor evaluates Primary Host `STATE`
  birth/death messages.
- ❌ **Consuming / decoding foreign messages.** PlugB does not subscribe to, decode, or
  interpret `BIRTH`/`DATA` payloads from other Edge Nodes or devices. It only receives the
  `NCMD`/`DCMD` commands addressed to itself.

These boundaries keep the library small, predictable, and easy to reason about. Internal
structures (e.g. an optional `PrimaryHostId` in the options) are already in place so that a
future host/consumer layer can be added **without breaking changes**.

### Roadmap
- [ ] Primary Host awareness (`STATE`) and store-and-forward gating.
- [ ] Sparkplug Host Application / consumer layer (decoding foreign BIRTH/DATA).
- [ ] Optional metric history / store-and-forward buffer (explicitly opt-in).

## 📦 Architecture

The library is split into four logical layers, hiding everything below the public API:

1. **Public API & DX**: `IPlugBClient`, `IPlugBDevice`, `PlugBClientBuilder`, `MetricBuilder`,
   `PlugBOptions`, `PlugBDataType`.
2. **Domain & Mapping**: `PayloadBuilder`, `TopicGenerator`, `DataTypeConverter` — translating
   the friendly C# world into the strict Sparkplug B specification.
3. **State & Sequence** *(internal)*: `SequenceManager` (`seq` + `bdSeq`), `DeviceRegistry`.
4. **Transport & Lifecycle** *(internal)*: `MqttTransport` over MQTTnet, connect/LWT logic,
   the serialized publish pipeline, and reconnect handling.

The generated Sparkplug B Protobuf types are kept strictly `internal` and never leak into the
public API.

## 🧪 Building & Testing

```bash
dotnet build
dotnet test
```

- **Unit tests** run broker-free: the transport is abstracted, so lifecycle, sequence, topic
  and payload logic are verified by inspecting the messages PlugB would emit.
- **Integration tests** spin up a real Mosquitto broker via
  [Testcontainers](https://dotnet.testcontainers.org/) (`Testcontainers.Mosquitto`) and
  therefore **require a running Docker engine**. They are skipped automatically when Docker is
  unavailable, so the unit-test run stays green.

## ▶️ Running the Sample

A runnable console demo lives in [`src/PlugB.Sample`](./src/PlugB.Sample). Its `Broker/`
folder ships a `docker-compose.yml` that starts a single, pre-configured Mosquitto broker —
the sample application itself is started separately. See the
[sample README](./src/PlugB.Sample/README.md) for details:

```bash
# 1. start the broker
docker compose -f src/PlugB.Sample/Broker/docker-compose.yml up -d
# 2. run the Edge Node sample
dotnet run --project src/PlugB.Sample
```

## ⚙️ Requirements

- **.NET 10 SDK** or later (`net10.0`).
- A Sparkplug-B-capable MQTT broker (Mosquitto, EMQX, HiveMQ, …).
- **Docker** — only for the integration tests and the sample broker.

## 📚 Dependencies & Licenses

PlugB intentionally builds on the battle-tested **MQTTnet** client rather than
reimplementing the MQTT protocol, and uses **Google.Protobuf** with the Eclipse Tahu
`sparkplug_b.proto` schema for payload encoding.

A complete list of bundled third-party components and their licenses is documented in:

📄 **[THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt)**

## 🤝 Contributing

Contributions are welcome! Please keep the public API free of MQTT/Protobuf types, add a unit
test for any change touching the lifecycle or sequence logic, and make sure `dotnet build`
stays warning-free (warnings are treated as errors).

## ⚖️ License

This project is licensed under the **Apache License 2.0**. See the [LICENSE](./LICENSE.txt)
file for details. You are free to use, modify, and distribute this software in **commercial**
and **private** applications.

---
*Built for clean IT/OT connectivity — so you can publish Sparkplug B without reading the
260-page spec first.*