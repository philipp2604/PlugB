# PlugB Sample 💥🔌

A minimal, runnable demonstration of **[PlugB](../../README.md)** acting as a complete Sparkplug B
ecosystem. It demonstrates both an **Edge Node** and a **Primary Host Application** interacting
with each other over a local Mosquitto broker. 

You can witness the full Sparkplug B lifecycle in action: Birth-Gating (waiting for the host),
Store-and-Forward (buffering data while offline), and automatic Rebirth requests.

## Prerequisites

- **.NET 10 SDK**
- **Docker** (for the bundled Mosquitto broker)

The sample needs a broker and the application running side by side. The broker is **not**
started by the sample — bring it up first, then run the app components in separate terminals.

## 1. Start the Broker

Start the local Mosquitto broker. The `Broker/` folder contains a `docker-compose.yml`
with a single, pre-configured Mosquitto service:

```bash
cd Broker
docker compose up -d
cd ..
```

*(Or, from the repository root: `docker compose -f src/PlugB.Sample/Broker/docker-compose.yml up -d`)*

## 2. Run the Edge Node (Terminal 1)

Open your first terminal and start the Edge Node:

```bash
dotnet run
```

*(Or, from the repository root: `dotnet run --project src/PlugB.Sample`)*

The console will show the Edge Node connecting and setting its `NDEATH`. However, because it is configured to wait for the Primary Host (`SCADA_1`), it will **not** publish its `NBIRTH` yet. Instead, it starts generating data and buffering it locally (Store-and-Forward).

## 3. Run the Host Application (Terminal 2)

Open a second terminal to start the Primary Host simulator. 

> **⚠️ Important:** You must use the `--no-build` flag for the second instance. Running `dotnet build` (which `dotnet run` does implicitly) simultaneously in two terminals causes file locking errors.

```bash
dotnet run --no-build --host
```

*(Or, from the repository root: `dotnet run --project src/PlugB.Sample --no-build --host`)*

**What happens now:**
1. The Host connects and publishes its `STATE` message (`online: true`).
2. The Edge Node detects the Host is online.
3. The Edge Node publishes its historical buffered data, followed by its `NBIRTH` and `DBIRTH`.
4. The Edge Node begins streaming live `DDATA`.
5. The Host receives, decodes, and logs the incoming data.

### Interactive Host Commands
While the Host is running, you can press:
- **`S`**: Prints a live snapshot of the Host's internal Namespace Registry (Nodes, Devices, and their current metric values).
- **`R`**: Sends a Node Control command (`NCMD`) requesting a Rebirth. You will see the Edge Node react immediately by republishing its birth certificates.
- **`Q`**: Gracefully shuts down the Host (publishes an offline `STATE`).

## Watch the Raw Traffic

To see the raw Sparkplug B topics on the wire, subscribe with any MQTT client. For example,
using the Mosquitto CLI tools:

```bash
mosquitto_sub -h localhost -p 1883 -t 'spBv1.0/#' -v
```

The payloads are Sparkplug B Protobuf, so a plain text subscriber will display them as binary.
Use a Sparkplug-aware tool (e.g., MQTT Explorer with a Sparkplug decoder, or Ignition) to
inspect the decoded metrics visually.

## Clean up

Stop both sample instances (Ctrl+C or `Q` in the Host), then shut the broker down:

```bash
cd Broker
docker compose down
```

## Configuration

Broker host, ports, Group ID, Edge Node ID, and Host ID are all configured using the Fluent Builders at the top of `Program.cs`. Adjust them there if you want to test against an external MQTT broker.