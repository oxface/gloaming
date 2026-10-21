# Messaging family

Explicit transactional outbox, retained inbox intake and local processing, with independently
selected sequential workers. Transport configuration and business contracts belong to the consumer.

| Package | Responsibility |
| --- | --- |
| [Gloaming.Messaging](Gloaming.Messaging/README.md) | Provider-free incoming/outgoing envelopes, publication interface and internal native trace/metric instrumentation. BCL only. |
| [Gloaming.Messaging.EntityFrameworkCore](Gloaming.Messaging.EntityFrameworkCore/README.md) | Provided records, mapping/save guards, typed roles, keyed handlers and opt-in workers. |
| [Gloaming.Messaging.EntityFrameworkCore.Postgres](Gloaming.Messaging.EntityFrameworkCore.Postgres/README.md) | JSONB/database-time mapping, outbox leases, inbox deduplication and locked transactional processing. |
| [Gloaming.Messaging.OpenTelemetry](Gloaming.Messaging.OpenTelemetry/README.md) | Optional AddGloamingMessaging extensions on native trace/metric builders; API-only dependency. |
| [Gloaming.Messaging.Workflows](Gloaming.Messaging.Workflows/README.md) | Optional typed continuation bindings, return instructions and detached snapshots. |
| [Gloaming.Messaging.Workflows.EntityFrameworkCore](Gloaming.Messaging.Workflows.EntityFrameworkCore/README.md) | Optional provided process table, typed runner, atomic request enqueue and native concurrency. |

Each module owns its context, schema/table, publisher and registrations. The family does
not supply a global bus, shared DbContext or cross-module transaction. Core publisher code
does not need EF; EF code does not reference Npgsql; PostgreSQL implements its native protocol.
Only PostgreSQL dispatch/intake/processing is supported. No generic provider dialect is introduced.

Start with [setup, guarantees and deferred capabilities](docs/capabilities.md). Executable
adopters are [Inventory/RabbitMQ](../../samples/Wholesale/EventPersistenceDemo/README.md)
and the [independent state-stored HTTP sender](../../samples/OutboxDemo/README.md),
[inbox-only receiver](../../samples/InboxDemo/README.md) and
[two-module RabbitMQ journey](../../samples/MessagingDemo/README.md), followed by
[separate API/worker hosts](../../samples/MessagingWorkerDemo/README.md) and
[Sales → Inventory request/reply progress](../../samples/Wholesale/WorkflowDemo/README.md) and
[two-line reservations with compensation](../../samples/Wholesale/WorkflowDemo/compensating-reservations.md).
See [inbox setup and transaction ownership](docs/inbox.md).
See [failed inbox work and authorized retry](docs/inbox-recovery.md) for durable failure
accounting, consumer-selected dead-letter state, revision-conditional redrive and upgrade obligations.
See [outbox dead letters and operator retry](docs/outbox-recovery.md) for publication-failure
observations, lease fencing, acceptance uncertainty, inspection, completion/accounting supervision
and restricted-grant upgrades.
See [outbox backlog and worker health](docs/outbox-observability.md) for the callable route
snapshot, native consumer gauges/readiness and metadata-only authority.
See [durable continuation setup and consumer ownership](docs/workflows.md) for the optional runner.

Tests run from the repository root:

```bash
dotnet test --project src/Gloaming.Messaging/tests/MessagingTests/MessagingTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/WorkflowTests/WorkflowTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/WorkflowPostgresTests/WorkflowPostgresTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/InboxPostgresTests/InboxPostgresTests.csproj
dotnet test --project samples/OutboxDemo.Tests/OutboxDemo.Tests.csproj
dotnet test --project samples/InboxDemo.Tests/InboxDemo.Tests.csproj
dotnet test --project samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj
dotnet test --project samples/Wholesale/WorkflowDemo.Tests/WorkflowDemo.Tests.csproj
```

PostgreSQL tests require a Docker-compatible runtime; Podman works via DOCKER_HOST pointing
to its user socket. Focused Inventory broker proofs additionally require RabbitMQ containers.
The inbox PostgreSQL suite shares one assembly-level container, creates a fresh database
for each test and runs at most two tests concurrently. The outbox suite retains its sequential
collection fixture; its measured parallel candidates did not show a reliable speed improvement.
Architecture tests remain repository-owned. [The O1 report](../../docs/reports/outbox1-transactional-dispatch.md)
separates its results from archived evidence; [I1's report](../../docs/reports/inbox1-durable-intake-processing.md)
records the new inbox/adoption proofs separately.

See [durable message observability](docs/observability.md) for optional W3C context, schema upgrades, native
activity/meter names, failure logs and host-owned exporter configuration.
