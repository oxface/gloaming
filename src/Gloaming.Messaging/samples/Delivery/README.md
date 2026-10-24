# Durable delivery example

One Exports producer API and one worker executable with separately selected dispatch,
receive and process roles. Exports and Rendering own separate DbContexts, schemas and
migration histories. Neither module references the other, a host, RabbitMQ or worker hosting.
The producer's dependency graph also excludes the optional Gloaming worker package.

| Code | Consumer responsibility |
| --- | --- |
| [Exports](Exports/ExportRequestCommands.cs) | Decide submission from tracked state; enqueue a directed command in the caller's transaction. Ordinary JSON, no event codec. |
| [Rendering](Rendering/RenderExportHandler.cs) | Validate the incoming command and apply local idempotent business effects. Opt into the existing codec for v1 → v2 decoding. |
| [Producer](Producer/Program.cs) | HTTP validation, native transaction/save/commit, and response mapping. |
| [Worker](Worker/Program.cs) | Explicit setup or one runtime role, polling, scopes and native StopHost supervision. |
| [Transport](Worker/Transport) | Consumer routing, metadata validation, publisher confirmation and acknowledgement after durable intake. |
| [ServiceDefaults](ServiceDefaults/Extensions.cs) | Editable native telemetry wiring; no AppHost or Gloaming runtime is required. |

## Run

Supply disposable PostgreSQL databases and a RabbitMQ broker; this example does not
provision services or authenticate HTTP callers. From the repository root:

```bash
export ConnectionStrings__Exports='Host=localhost;Database=exports_demo;Username=demo;Password=demo'
export ConnectionStrings__Rendering='Host=localhost;Database=rendering_demo;Username=demo;Password=demo'
export RabbitMQ__Uri='amqp://guest:guest@localhost:5672/'
export RabbitMQ__Queue='exports.render'

dotnet run --project src/Gloaming.Messaging/samples/Delivery/Worker/Messaging.Worker.csproj -- --role setup
```

Setup is a finite operation: apply module migrations and declare the durable queue. It
seeds no business work. Run the producer and each worker role in separate terminals with
the settings above:

```bash
export ASPNETCORE_URLS='http://127.0.0.1:5080'
dotnet run --project src/Gloaming.Messaging/samples/Delivery/Producer/Messaging.Producer.csproj
dotnet run --project src/Gloaming.Messaging/samples/Delivery/Worker/Messaging.Worker.csproj -- --role dispatch
dotnet run --project src/Gloaming.Messaging/samples/Delivery/Worker/Messaging.Worker.csproj -- --role receive
dotnet run --project src/Gloaming.Messaging/samples/Delivery/Worker/Messaging.Worker.csproj -- --role process
```

```bash
curl -i http://127.0.0.1:5080/exports -H 'Content-Type: application/json' \
  -d '{"id":"c2f79e58-17d2-4b8c-aa1a-e9d6da90cbee","pages":3}'
curl -i http://127.0.0.1:5080/exports/c2f79e58-17d2-4b8c-aa1a-e9d6da90cbee/submit \
  -H 'Content-Type: application/json' -d '{"expectedVersion":1}'
```

Submission commits the business change and outgoing envelope together. The API can then
stop; dispatch publishes, receive commits the inbox before acknowledgement, and process
commits a RenderJob with completion of the retained delivery. Runtime roles require existing
tables/queue and do not run migrations. Deploy each role with only its required connection
strings and queue authority.

## Guarantees and limits

The worker host selects native StopHost supervision and one operation at a time per role.
Dispatch leases default to 30 seconds (`Worker__LeaseSeconds`); native publication has a
five-second deadline. Idle polling is 100 ms, ordinary failure delay one second. Rendering
owns its failure budget and classification; dispatch keeps indefinite publisher retries.
Broker sessions disable automatic recovery and stop their host after terminal failure.
The deployment owns restart/backoff, credentials/TLS, admission and shutdown deadlines.

A crash after publication can repeat a delivery. Retained inbox identity suppresses repeated
local handling; Rendering's business key independently prevents duplicate jobs. There is no
external exactly-once guarantee. Upgrade readers before selecting schema v2 in producer
wiring; the default HTTP producer writes v1. The library never rewrites retained payloads.

Set `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_SERVICE_NAME` to select native OTLP export.
The hosts subscribe directly to `Gloaming.Messaging` sources/meters and native RabbitMQ/Npgsql
instrumentation. See [observability](../../docs/observability.md),
[worker recovery](../../docs/worker-recovery.md), [schema compatibility](../../docs/schema-compatibility.md)
and the [supported capability limits](../../docs/capabilities.md).

## Proof ownership

```bash
dotnet test --project src/Gloaming.Messaging/tests/DeliveryTests/DeliveryTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/WorkflowDeploymentTests/WorkflowDeploymentTests.csproj
```

The delivery suite retains transaction/adoption, migration, schema, broker, operational and
process proofs. Linux child processes run against real PostgreSQL/RabbitMQ containers;
OTLP proofs also start a real Collector. Set `DOCKER_HOST` to the user socket for rootless
Podman. Tests use private databases/queues and terminate only their own processes.

Earlier HTTP/operator/maintenance hosts and finite broker journeys are now
[test fixtures](../../tests/Fixtures/README.md), rather than additional advertised applications.
They retain explicit auth, restricted-grant, retention and failure-injection evidence.
The independently compiled [workflow deployment fixtures](../../tests/Fixtures/WorkflowDeployment/README.md)
retain literal historic input, state and reply JSON. Their test hooks are consumer proof code.
