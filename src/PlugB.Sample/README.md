# PlugB Sample 🔌

A minimal, runnable demonstration of **[PlugB](../README.md)** acting as a Sparkplug B
Edge Node. It connects to a local Mosquitto broker, publishes an `NBIRTH` and a device
`DBIRTH`, and then streams `DDATA` updates — with all sequence and lifecycle handling done by
the library.

## Prerequisites

- **.NET 10 SDK**
- **Docker** (for the bundled Mosquitto broker)

The sample needs a broker and the application running side by side. The broker is **not**
started by the sample — bring it up first, then run the app in a separate step.

1. Start the local Mosquitto broker. The `Broker/` folder contains a `docker-compose.yml`
   with a single, pre-configured Mosquitto service:

   ```bash
   cd Broker
   docker compose up -d
   cd ..
   ```

   (Or, from the repository root, without changing directory:
   `docker compose -f src/PlugB.Sample/Broker/docker-compose.yml up -d`.)

2. Run the sample application:

   ```bash
   dotnet run
   ```

   (Or, from the repository root: `dotnet run --project src/PlugB.Sample`.)

3. The console will show the Edge Node connecting, sending its `NBIRTH` + `DBIRTH`, and
   publishing periodic `DDATA` messages.

## Watch the traffic

To see the raw Sparkplug B topics on the wire, subscribe with any MQTT client. For example,
using the Mosquitto CLI tools:

```bash
mosquitto_sub -h localhost -p 1883 -t 'spBv1.0/#' -v
```

You should see topics such as:

```
spBv1.0/Factory_01/NBIRTH/EdgeGateway_A
spBv1.0/Factory_01/DBIRTH/EdgeGateway_A/PLC_Machine_1
spBv1.0/Factory_01/DDATA/EdgeGateway_A/PLC_Machine_1
```

The payloads are Sparkplug B Protobuf, so a plain text subscriber will display them as binary.
Use a Sparkplug-aware tool (e.g. MQTT Explorer with a Sparkplug decoder, or Ignition) to
inspect the decoded metrics.

## Clean up

Stop the sample (Ctrl+C), then shut the broker down:

```bash
cd Broker
docker compose down
```

If you stop the sample without a graceful shutdown, the broker will publish the Edge Node's
`NDEATH` (the Last Will & Testament) — exactly as the Sparkplug B specification requires.

## Configuration

Broker host, port, Group ID and Edge Node ID are set at the top of `Program.cs`. Adjust them
there if you point the sample at a different broker.