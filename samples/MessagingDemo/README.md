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

The adapters bind the known exports producer and exact schema. Broker permissions/trusted
producer mapping are consumer obligations, not authenticated by a producer header. Production
consumers own prefetch, subscriptions, reconnect, rejection/dead-letter policy and scaling.

Real broker proofs close the channel after committed intake but before native ack, fail intake
before commit, fail sender completion after confirmed publication, and fail local processing
after acknowledged intake. Redelivery retains identity; fresh processing recovers without
repeating a completed job. Tests terminate no production infrastructure.
[Supported protocol](../../src/Gloaming.Messaging/docs/inbox.md).

For a long-running API plus separate dispatch/intake/processing processes using these same
adapters, see [the worker host](../MessagingWorkerDemo/README.md). The finite journey remains
available; it is not the new worker startup path.

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../src/Gloaming.Messaging/docs/observability.md).
