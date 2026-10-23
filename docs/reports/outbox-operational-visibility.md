# Outbox operational visibility

Date: 2026-10-21. Owner-selected follow-up after approving completion supervision,
checkpoint `020a292`. That checkpoint passed every repository commit hook. This slice is
implemented on `feat/outbox-operational-visibility` for owner line-by-line review; the
[scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/outbox-operational-visibility.md) records concrete public usage and file/schema
boundaries. Earlier [completion](m2-outbox-completion-supervision.md), [dead-letter](m2-outbox-dead-letters.md)
and [attempt telemetry](obs1-durable-message-observability.md) results remain separate evidence.

## Outcome and extraction decision

One callable ReadPostgresOutboxBacklogAsync context extension returns a detached
OutboxBacklogSnapshot for one admitted route. A single aggregate SELECT uses one database
statement time. Pending is partitioned into Eligible, Delayed and Leased; DeadLetters is
separate, completed work is excluded, and nullable OldestEligibleAge measures time since
original insertion of the oldest currently eligible row. The read acquires no claims/row
locks and changes no durable counters or eligibility.

This is a new reusable observation mechanism, independently adopted by the tenantless
Exports host and the restricted Sales console. Native model validation/identifier quoting,
fresh-context guards and parameter helpers are reused because their semantics match.
The fresh-context helper only gains an operation-specific diagnostic. No recovery interface,
queue engine or provider-neutral promise is added.

Health, sampling, instrument names, topology and alert thresholds remain consumer-owned.
Native HealthCheckService, Meter/IMeterFactory and IHostedService cover those obligations;
no reusable health/polling framework was proven. The template is unchanged. No migration,
index, permission expansion, package dependency or transport protocol change was needed.
Existing accounting/completion uncertainty, Attempts and operator retry semantics remain intact.

## Executable adoption

[OutboxDemo](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo/README.md#backlog-and-health) adds the existing operator
credential's `/operations/outbox/status`, fixing exports.render. It returns a fresh snapshot
with no-store caching; storage faults return 503 without details. Public native health endpoints
separate liveness from fresh-storage/expected-worker readiness. Faulted/stopped workers are
unready under Ignore while the host remains live. Idle/delayed work is healthy by sample policy.

Its consumer-owned sampler uses native cached observable gauges with no dimensions. Callbacks
perform no I/O. Query faults preserve the last successful observation and increase its
monotonic age; no success means absent/unknown gauges. The age includes successful-query
latency. A subsequent successful read replaces the sample. Each sample read has a requested
three-second deadline, followed by the configurable interval (five seconds by default).
Readiness independently reads storage instead of trusting that cache.

[Restricted Sales inspection](../../samples/Wholesale/Worker/Broker/sales-outbox-dispatch.md#backlog-inspection)
prints independent snapshots for three fixed command routes using the existing verified
dispatch credential, without starting dispatch or opening a broker. The library read also
works under a separate six-column SELECT-only role. Native grants/RLS define visible rows;
EF filters do not authorize this raw SQL operation. No payload/identity SELECT or UPDATE is
needed. Consumer code authorizes route and credential/RLS context before invocation.

## New proofs and verification

Tests use real PostgreSQL 18.6, actual Kestrel HTTP, public callable/mapped-row boundaries,
native MeterListener/ILogger/health/task APIs and built child processes. Newly exercised cases:

- Empty routes return observed zero counts and absent age without dispatcher registration.
  Mixed pending, delayed, live/expired leased, dead-lettered, completed and other-route work
  has independently expected counts. Lease precedence and later expiry preserve the partition;
  age equals database observation time minus the known oldest insertion.
- An observation completes while another transaction holds a row update and uncommitted
  insertion. It sees prior committed state, then sees the new state after commit. No claim or
  revision changes are made by reads.
- A six-column SELECT-only login sees only its RLS-admitted tenant. Actual payload SELECT and
  lifecycle UPDATE attempts are denied. Tracked/pending changes and native/ambient transactions
  reject independent observation rather than silently changing its meaning.
- HTTP status enforces authorization and fixes the route despite query input. Successful JSON
  excludes payload, identity, tenant, lease token, tracing and exception data. Missing schema
  and an unreachable database produce unready/503 status with process liveness intact and
  absent gauges, not fabricated zeros.
- A completion-faulted worker under native Ignore remains stopped and unready while HTTP
  stays live; delayed work is ready, and explicitly stopping its idle worker makes it unready.
- Native metric callbacks finish while an exclusive table lock is held. Sampling SQL errors
  are observed through the native warning log; cached counts remain with increasing age.
  Repair and a new enqueue produce fresh counts/readiness. Meter-factory scoping prevents
  cross-host/test contamination and measurement tags remain empty.
- A built Sales inspection child, configured without any RabbitMQ connection, sees both
  tenants under its restricted dispatch login, reports three routes and leaves Attempts,
  Revision and leases untouched. Existing privilege-denial/accounting-supervision proofs
  remain passing regressions.

Fresh successful final runs, with zero failures/skips:

| Suite | Passed |
| --- | ---: |
| OutboxPostgresTests, complete | 90 |
| OutboxDemo.Tests, complete | 18 |
| WorkflowDemo.Tests, SalesDispatchTests | 35 |
| MessagingWorkerDemo.Tests, complete | 13 |
| ArchitectureTests, complete | 96 |
| **Total** | **252** |

The active solution build passed with zero warnings/errors. Full solution style/analyzer
verification, CSharpier (891 files including generated source), git diff --check and final
Markdown/local-link/whitespace checks all passed. The eight backlog cases,
six HTTP/native-metrics cases and one restricted process case are new; the other tests are
fresh regression runs, including real RabbitMQ/OTLP and accounting/completion/fencing proofs.
The existing OTLP proof does not certify export of the new sample-owned gauges.

Development first reproduced absent snapshot/status/sampler behavior. Initial verification
also caught a misplaced test using directive, a listener accidentally collecting unrelated
native HTTP instruments, and EF's transient connection-failure wrapper escaping the first
503 catch. These were corrected before the successful final runs; failed runs are not counted
as passing evidence. Native listeners now select both the consumer meter and its factory scope.

Reproduce from the repository root with the configured Docker-compatible runtime:

```bash
dotnet build ModulithFoundry.slnx --no-restore
dotnet test --project src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Messaging/samples/OutboxDemo.Tests/OutboxDemo.Tests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-build --no-restore -- --filter-class '*SalesDispatchTests'
dotnet test --project src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj --no-build --no-restore
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj --no-build --no-restore
dotnet format style ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet format analyzers ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet csharpier check . --include-generated
git diff --check
```

## Review order and remaining limits

Review the [DTO](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxBacklogSnapshot.cs)
and [provider read](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDiagnosticsExtensions.cs),
then [native health](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo/ExportOutboxHealthCheck.cs),
[sampling/gauges](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo/ExportOutboxMetrics.cs), status authorization and the
Sales command. Follow with the [PostgreSQL](../../src/Gloaming.Messaging/tests/OutboxPostgresTests/BacklogTests.cs),
[HTTP/metrics](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo.Tests/OperationalVisibilityTests.cs) and
[restricted process](../../samples/Wholesale/IntegrationTests/Reservations/Broker/SalesDispatchTests.Visibility.cs) proofs.
The [family contract](../../src/Gloaming.Messaging/docs/outbox-observability.md) makes integration
obligations discoverable beside the implementation.

Snapshots are immediately stale observations of admitted visible rows, not reservations,
global empty-queue assertions or acceptance evidence. Gauges are cached; separate callbacks
can straddle observations, and replicas may observe the same work. A running task does not
prove transport availability or progress. The dedicated host's expected-worker assumption
must be adapted for larger compositions. Alerts, exporters, readiness thresholds and external
restart policy remain consumer operations. Aggregate query cost and cadence need workload
assessment; no dashboard, exporter or capacity certification was added.

Inbox backlog, durable telemetry/automatic alerts, retention, queued-schema rollout, lease
renewal and dispatch parallelism remain separate capabilities. Payload editing and completed
message replay remain excluded. This slice stays unstaged/uncommitted with the originally
empty index preserved. No push or pull request was requested.
