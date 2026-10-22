# Messaging family

Explicit transactional outbox, retained inbox intake and local processing, with independently
selected sequential workers. Transport configuration and business contracts belong to the consumer.

| Package | Responsibility |
| --- | --- |
| [Gloaming.Messaging](Gloaming.Messaging/README.md) | Provider-free incoming/outgoing envelopes, publication interface and internal native trace/metric instrumentation. BCL only. |
| [Gloaming.Messaging.EntityFrameworkCore](Gloaming.Messaging.EntityFrameworkCore/README.md) | Provided records, mapping/save guards, typed callable roles and keyed handlers; no worker/Hosting dependency. |
| [Gloaming.Messaging.EntityFrameworkCore.Hosting](Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md) | Optional sequential workers, polling options, fresh scopes and native host supervision. |
| [Gloaming.Messaging.EntityFrameworkCore.Postgres](Gloaming.Messaging.EntityFrameworkCore.Postgres/README.md) | JSONB/database-time mapping, outbox leases, inbox deduplication and locked transactional processing. |
| [Gloaming.Messaging.RabbitMQ](Gloaming.Messaging.RabbitMQ/README.md) | Optional native connection/channel ownership, phase timeouts, terminal observation and cleanup; independent of core/EF/Generic Host. |
| [Gloaming.Messaging.Workflows.EntityFrameworkCore](Gloaming.Messaging.Workflows.EntityFrameworkCore/README.md) | Optional bounded continuation definitions and native EF execution: provided process table, typed runner, atomic request enqueue and concurrency. |

Each module owns its context, schema/table, publisher and registrations. The family does
not supply a global bus, shared DbContext or cross-module transaction. Core publisher code
does not need EF; EF code does not reference Npgsql; PostgreSQL implements its native protocol.
Only PostgreSQL dispatch/intake/processing is supported. No generic provider dialect is introduced.

Start with [setup, guarantees and deferred capabilities](docs/capabilities.md) and the
[durable delivery example](samples/Delivery/README.md). It keeps module persistence separate,
with an API-only producer and explicitly selected worker roles. Additional executable
[consumer policy and deployment fixtures](tests/Fixtures/README.md) belong to the tests.
Wholesale provides the larger [request/reply composition](../../samples/Wholesale/Worker/Broker/README.md).
See [inbox setup and transaction ownership](docs/inbox.md).
See [worker recovery and shutdown](docs/worker-recovery.md) for shared RabbitMQ lifecycle
mechanics, consumer-selected fail-stop/native recovery, restart and cancellation obligations.
See [failed inbox work and authorized retry](docs/inbox-recovery.md) for durable failure
accounting, consumer-selected dead-letter state, receipt-generation/revision-conditional retry
and upgrade obligations.
See [outbox dead letters and operator retry](docs/outbox-recovery.md) for publication-failure
observations, lease fencing, acceptance uncertainty, inspection, completion/accounting supervision
and restricted-grant upgrades.
See [outbox backlog and worker health](docs/outbox-observability.md) for the callable route
snapshot, native consumer gauges/readiness and metadata-only authority.
See [completed-outbox retention](docs/outbox-retention.md) for an authorized, finite cleanup
batch, separate maintenance credentials and the sender identity-history boundary.
See [processed-inbox retention](docs/inbox-retention.md) for finite cleanup, receipt generations
and the explicit possibility of delivery re-execution once duplicate evidence is deleted.
See [retained-message schema compatibility](docs/schema-compatibility.md) for an optional existing
JSON codec, original-envelope deduplication and a concrete reader-before-writer consumer rollout.
See [durable continuation setup and consumer ownership](docs/workflows.md) for the optional runner.

Tests run from the repository root:

```bash
dotnet test --project src/Gloaming.Messaging/tests/MessagingTests/MessagingTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/WorkflowTests/WorkflowTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/WorkflowPostgresTests/WorkflowPostgresTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/InboxPostgresTests/InboxPostgresTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/DeliveryTests/DeliveryTests.csproj
dotnet test --project src/Gloaming.Messaging/tests/WorkflowDeploymentTests/WorkflowDeploymentTests.csproj
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
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

See [outbox route selection](docs/route-selection.md) when independently composed publishers share one module outbox.
