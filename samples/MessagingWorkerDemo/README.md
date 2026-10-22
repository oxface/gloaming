# Separately hosted messaging workers

A native .NET Generic Host composing the existing Exports outbox and Rendering inbox.
Each invocation selects one explicit role. The producer API, dispatcher, broker intake and
inbox processor can have separate deployment lifetimes and replica counts. These are editable
consumer entry points, using existing Gloaming APIs; there is no new worker runtime package.

| Role | Dependencies and behavior |
| --- | --- |
| `setup` | Exports + Rendering connections and RabbitMQ. Applies existing migrations and declares the configured durable queue, then exits. No business rows are seeded. |
| `dispatch` | Exports connection and RabbitMQ. Existing outbox worker claims leased work, publishes persistent messages with native confirmations, then records acceptance. |
| `receive` | Rendering connection and RabbitMQ. Sequential native intake commits retained work before acknowledging the delivery. No business handler executes here. |
| `process` | Rendering connection only. Existing inbox worker invokes the registered Rendering handler and atomically saves a job and delivery completion. No broker or Exports settings are required. |

The host alone composes the modules. It reuses the [finite journey's native broker adapters](../MessagingDemo/README.md)
and the modules' typed contexts. The receiver's own wire decoder/admission rules remain in
Rendering. No shared DbContext, peer business-table query or cross-module transaction appears.

## Setup and execution

Use existing PostgreSQL and RabbitMQ instances with two already-created databases and
appropriate credentials. The two modules may also use separate schemas in one database.
Native .NET configuration uses environment variables here; do not commit
credentials. From the repository root:

```bash
export ConnectionStrings__Exports='Host=localhost;Database=exports_demo;Username=demo;Password=demo'
export ConnectionStrings__Rendering='Host=localhost;Database=rendering_demo;Username=demo;Password=demo'
export RabbitMQ__Uri='amqp://demo:demo@localhost:5672'
export RabbitMQ__Queue='exports-rendering'
dotnet run --project samples/MessagingWorkerDemo/MessagingWorkerDemo.csproj -- --role setup
```

Then start these in separate terminals or supervised processes, giving each only the settings
listed for its role:

```bash
dotnet run --project samples/MessagingWorkerDemo/MessagingWorkerDemo.csproj -- --role dispatch
dotnet run --project samples/MessagingWorkerDemo/MessagingWorkerDemo.csproj -- --role receive
dotnet run --project samples/MessagingWorkerDemo/MessagingWorkerDemo.csproj -- --role process
```

Submit work through the [producer API](../MessagingProducerDemo/README.md). A RenderJob is
local durable work; recording one does not claim that an external renderer completed.
Queue declaration and migrations are finite setup, not worker startup. Setup can be rerun
against the same schema/queue definition. It performs separate module migrations, not one
atomic cross-module deployment. Workers read-check their required tables and passively check
an existing queue. They neither migrate/seed nor delete the durable queue when stopping.

`Worker__LeaseSeconds` configures the dispatch lease (default 30 seconds); native sample
publication has a five-second deadline. A lease is not an external exactly-once promise.
Both library workers poll idle work at 100 ms and delay ordinary processing failures for one
second. Inbox/outbox failure-accounting faults and outbox completion faults instead fault
their worker without another poll; native host supervision applies.
Generic Host owns Ctrl+C/SIGTERM cancellation and its default shutdown deadline. An abrupt
process death follows the storage protocol, rather than requiring a graceful callback.
This console executable uses `Microsoft.AspNetCore.App` for native Hosting/DI APIs and its
existing receiver dependency; install that shared framework even though it exposes no HTTP server.

## Recovery and operational limits

- Processing holds `FOR UPDATE SKIP LOCKED` through local handling and commit. Competing
  processes take different available deliveries. Abrupt death disconnects and rolls back
  that transaction; another process can retry its retained delivery.
- The processing role adopts Rendering's explicit failure policy: recognized PostgreSQL
  serialization/deadlock failures stop at three recorded failures; conflicts/unclassified
  failures are dead-lettered immediately. Counts and dead-letter state survive process restarts. A killed attempt
  may leave no count. The optional [InboxDemo operator host](../InboxDemo/README.md#failed-work-and-authorized-operator-retry)
  can use the same receiving database for authenticated, receipt-generation/revision-conditional retry. The worker
  itself exposes no operator endpoint. Apply the forward migration with old processors stopped;
  see [the complete recovery contract](../../src/Gloaming.Messaging/docs/inbox-recovery.md).
- Rendering registration explicitly selects native StopHost supervision. An
  InboxFailureAccountingException logs a critical diagnostic and stops the process host;
  it does not repeatedly invoke the handler or automatically dead-letter the delivery.
  Repair the accounting dependency before restarting, then inspect fresh storage: a lost
  accounting-commit response may have committed metadata. An unrecorded failure needs no
  operator redrive unless the row is actually dead-lettered. No specific exit code is promised,
  and unrelated work on this worker pauses until restart. Deployment monitoring/restart
  policy remains consumer-owned.
- Dispatch releases the database lock after committing a lease. Death after broker acceptance
  but before completion leaves the lease to expire. A replacement may publish the same
  MessageId again; retained inbox deduplication suppresses repeated local handling.
- OutboxCompletionException means publication returned normally but completion could not be
  confirmed. The dispatch worker logs a distinct critical diagnostic and faults; native
  StopHost begins host shutdown. Inspect fresh storage after repair before restarting:
  completion may already have committed. A pending claim waits for expiry and can repeat
  the same delivery; completion faults never consume publisher-failure counts or automatically
  dead-letter work. Other dispatchers and external restarts remain consumer supervision policy.
- Graceful stopping cancels native operations and disposes this process's long-lived connection
  and channel. Unacknowledged deliveries are recoverable on channel closure. Queues remain durable.
- Broker connections are established once and automatic recovery is disabled. Initial connection
  failure fails startup. Intake failure leaves work unacknowledged, logs the failure and exits
  nonzero; a supervisor/operator must restart it. The existing dispatcher retries publication
  failures in fresh database scopes; a permanently closed channel needs an operator/supervisor
  restart. This sample supplies no transport health monitor or deployment supervisor.

Each role performs one operation at a time. Replica counts allow independent scaling, but
these proofs establish correctness under competition rather than a throughput target. Native
subscriptions/prefetch/backpressure, reconnect, poison handling/dead-letter/redrive, lease
renewal, leader election, batching, deployment manifests and resource limits are not supplied.
Permissions/TLS and trusted producer mapping remain deployment/consumer obligations. A
malformed head delivery can require operator intervention; this sample does not silently discard it.
Optional retained W3C context, native attempt spans/logs/metrics and RabbitMQ transport
tracing are supported by OBS1. No root Gloaming runtime is required.

## Executable proofs

```bash
dotnet test --project samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj
```

Linux process tests use SIGTERM and hard process termination, real PostgreSQL 18.6 and
RabbitMQ 4.3.6 containers. Docker or rootless Podman is required; for Podman set `DOCKER_HOST`
to its user socket. Each test gets new module databases and a private queue. Tests start the
built executables directly; no external API/broker processes or personal credentials are required.
Database barriers hold an inserted, uncommitted job or an outbox completion after confirmed
publication. SIGTERM is also tested during the blocked job write, without releasing its
barrier first. These establish actual in-flight work before termination, rather than relying
on arbitrary sleeps or sample-only fault settings. Proof deadlines and child-process cleanup
are bounded. The API can exit before any worker starts, and the processor is run without
sender/broker configuration. See [the W1 report](../../docs/reports/w1-separate-worker-hosts.md)
for dated results and the exact supported boundary.

## Native telemetry export

The producer and each worker role call the existing editable
[ServiceDefaults](../Wholesale/ServiceDefaults/README.md), then add role resources and
Gloaming subscriptions through optional AddGloamingMessaging extensions on native trace
and metric builders in Program.cs. RabbitMQ sources remain explicitly selected there. There
is no separate AddTelemetry helper or Gloaming host package. Set these for processes that
should export to an existing Collector or Aspire dashboard OTLP endpoint:

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT='http://localhost:4318'
export OTEL_EXPORTER_OTLP_PROTOCOL='http/protobuf'
```

Defaults are service names messaging-producer, messaging-dispatch, messaging-receive and
messaging-process, with a new instance ID per process. OTEL_SERVICE_NAME can override the
name per deployment. Setup remains finite and configures no exporter. Hosts own sampling,
filtering, collection endpoints and retention. There is no required dashboard or Collector.

Follow the producer activity linked by each gloaming.outbox.dispatch attempt, its native
RabbitMQ send child, then the send context linked by gloaming.inbox.process. Different retry
spans preserve the same MessageId. Inspect error logs by trace/span ID and native attempt
counters/duration by operation/result. Metrics do not provide pending-work gauges or alerts.
The [family contract](../../src/Gloaming.Messaging/docs/observability.md) describes schema
upgrades and exact span/measurement boundaries.

The process suite now also uses a test-owned official OTel Collector 0.162.0 to assert actual
exported spans, logs and metrics. It forces post-publication completion and transactional
handler failures, restarts in fresh processes, and verifies duplicate intake preserves its
first context. An unreachable-collector case verifies business progress independently.
This is OTLP export evidence, not an Aspire dashboard inspection or throughput benchmark.

The process suite also launches the built optional OutboxDemo HTTP/worker host. Its
[outbox recovery proofs](../MessagingWorkerDemo.Tests/OutboxRecoveryProcessTests.cs) cover
death during accounting, persisted failure budgets and dead letters across restart, authorized
retry, and accounting SQL fault → StopHost → repair/restart under the same message identity.
The existing RabbitMQ dispatch role retains indefinite retry policy; HTTP classification stays
with its adopter. See the [outbox contract](../../src/Gloaming.Messaging/docs/outbox-recovery.md)
for counts, lease fencing, completion uncertainty and rollout/grant obligations.

## Message schema deployment

The existing dispatch, receive and process roles now understand exports.render schemas 1 and 2.
Schema 2 renames Pages to PageCount with the same business meaning. Upgrade all three roles
across every instance before a producer selects v2. The default producer API continues writing
v1; explicit application wiring or the [finite compatibility journey](../MessagingDemo/README.md#schema-compatibility-journey)
selects v2. No extra worker role or scheduling setting is required.

Rendering opts into the existing optional JSON codec; the stored schema/payload remain unchanged.
The process suite seeds independent historical v1 outbox/inbox JSON plus a new v2 submission,
kills dispatch/processing at native barriers, and recovers through replacement roles after real
lease expiry. Keep compatible readers deployed when rolling a producer back to v1. Reader
downgrade, retention and deployment coordination remain consumer obligations; see the
[schema compatibility contract](../../src/Gloaming.Messaging/docs/schema-compatibility.md).

## Inbox receipt maintenance

Processing hosts retain receipts until separately authorized maintenance removes eligible
processed work. [InboxDemo --cleanup](../InboxDemo/README.md#processed-inbox-cleanup) is one finite
consumer command with separate metadata-only DELETE credentials, explicit cutoff and batch.
Consumer infrastructure owns its schedule; do not add DELETE to processing/intake/operator
credentials or silently prune during processing. Pending/processing/dead-lettered work remains.
After receipt deletion an old broker delivery can execute again; retain semantic business
idempotency where required. ReceiptId/revision pairs prevent saved operator requests from
retrying a later receipt generation. Upgrade all intake/operator binaries before enabling cleanup.
