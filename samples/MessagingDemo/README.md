# Separate-module durable RabbitMQ journey

A finite console composition of [Exports](../OutboxDemo/README.md) and
[Rendering](../InboxDemo/README.md). Each owns its own context, schema and migration history.
They can use one database with separate schemas or entirely separate PostgreSQL databases;
no shared connection/transaction or peer persistence access is used. The host alone references
both implementations; the receiver knows the wire alias/schema, not the sender's CLR DTO.

The journey explicitly migrates disposable schemas, submits an eligible export and enqueues
its command in the sender transaction. A consumer-owned native RabbitMQ publisher uses durable
queue/persistent mandatory publication with confirmation tracking. The receiver parses and admits
metadata, commits unique intake and only then manually acknowledges the delivery on that channel.
Later local processing atomically records a RenderJob and inbox completion. Recording a job
does not claim external rendering completed.

```bash
export MESSAGING_DEMO_SENDER_CONNECTION_STRING='Host=localhost;Database=exports_demo;Username=demo;Password=demo'
export MESSAGING_DEMO_RECEIVER_CONNECTION_STRING='Host=localhost;Database=rendering_demo;Username=demo;Password=demo'
export MESSAGING_DEMO_RABBITMQ='amqp://demo:demo@localhost:5672'
dotnet run --project samples/MessagingDemo/MessagingDemo.csproj
dotnet test --project samples/InboxDemo.Tests/InboxDemo.Tests.csproj
```

The sample owns a private durable queue for its finite run and removes it after acknowledged
delivery. It dispatches/processes at most one eligible retained row, which may be older work
when run on databases with a backlog. Use disposable databases for the demonstrated single-job
output. No broker/transport runtime moves into Gloaming.

The adapters bind the known exports producer and explicitly supported schemas 1 and 2. Broker permissions/trusted
producer mapping are consumer obligations, not authenticated by a producer header. Production
consumers own prefetch, subscriptions, reconnect, rejection/dead-letter policy and scaling.

Real broker proofs close the channel after committed intake but before native ack, fail intake
before commit, fail sender completion after confirmed publication, and fail local processing
after acknowledged intake. Redelivery retains identity; fresh processing recovers without
repeating a completed job. Tests terminate no production infrastructure.
[Supported protocol](../../src/Gloaming.Messaging/docs/inbox.md).

## Schema compatibility journey

Use fresh disposable empty module databases with the same connection/broker settings above:

```bash
dotnet run --project samples/MessagingDemo/MessagingDemo.csproj -- --schema-compatibility
```

This finite mode submits v1, dispatches it and commits intake without processing. After disposing
the initial scopes, it explicitly selects v2 for a second submission. Fresh receiving/processing
scopes interpret both versions and create two local jobs. The journey checks original retained
schemas/payloads in both databases and prints message IDs plus:

```text
retained schemas=1,2; local jobs=2; original envelopes unchanged
```

V1 uses ExportRequestId/Pages; v2 renames Pages to PageCount without changing business meaning.
Rendering independently adopts the existing optional JSON codec. The original stored/wire
envelope remains the inbox's duplicate-comparison evidence; the producer stays codec-free.
Reusing the databases rejects rather than replaying completed work. The default single-v1 journey
remains available; no long-running host, migration worker or deployment coordinator is added.

Upgrade publishers, intake adapters and processors before selecting v2 writes. Keep v1 support
for delayed/redriven work; rolling readers back to v1-only code remains unsafe while v2 work
can arrive. Real process tests additionally kill dispatch/processing while historical v1 and new
v2 work is retained, then recover through the existing roles and lease/locking protocols.
The [family contract](../../src/Gloaming.Messaging/docs/schema-compatibility.md) records exact
deduplication, rejection, operator retry and rollout/rollback limits.

For a long-running API plus separate dispatch/intake/processing processes using these same
adapters, see [the worker host](../MessagingWorkerDemo/README.md). The finite journey remains
available; it is not the new worker startup path.

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../src/Gloaming.Messaging/docs/observability.md).
