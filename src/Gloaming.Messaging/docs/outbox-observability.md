# Outbox backlog and worker health

Read a route's visible retained work without claiming, publishing or registering recovery:

```csharp
await using var scope = services.CreateAsyncScope();
var database = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
OutboxBacklogSnapshot backlog = await database.ReadPostgresOutboxBacklogAsync(
    "exports.render", cancellationToken);
```

Reference the PostgreSQL package and its namespace. The extension validates the configured
outbox model and owns a short native ReadCommitted transaction on a fresh context, without
tracked entities or existing native/enlisted/ambient transactions. It parameterizes one exact
nonblank route selected by admitted consumer code. No producer, publisher, worker, logging,
health, metrics or recovery registration is required. SQL/permission/cancellation failures
throw; they do not fabricate an empty result.

## Snapshot meaning and authority

One aggregate SELECT uses a single PostgreSQL statement timestamp for all comparisons.
The result contains no message identity, envelope, payload, owner, lease token, failure code
or exception detail:

| Property | Meaning |
| --- | --- |
| ObservedAt | Database statement time used to classify visible rows. |
| Pending | Undispatched work excluding dead letters: Eligible + Delayed + Leased. |
| Eligible | Pending work available by ObservedAt, with no unexpired lease. |
| Delayed | Pending work with future availability and no unexpired lease. |
| Leased | Pending work with an unexpired lease, regardless of availability. |
| DeadLetters | Undispatched stopped work, separate from Pending. |
| OldestEligibleAge | ObservedAt minus the oldest original QueuedAt among eligible rows; null if none, clamped at zero. |

Age includes time spent in earlier leases, delays and operator-retry cycles. It is not a
continuous waiting duration, confirmation of transport acceptance or end-to-end latency.
Expired leases move into Eligible or Delayed according to availability. Completed rows are
excluded. A retained dead letter can still have an obsolete external publisher in flight.

The read takes no row locks or leases and observes committed state through native MVCC.
It can include a row another transaction currently locks, and its result may become stale
immediately. Eligible is a state classification, not a promise that a dispatcher can acquire
that row next. Separate route/host observations have no shared snapshot. NoWork still does
not mean an empty backlog. Observation changes no Attempts, Revision, failure or retry state.

Native SQL grants/RLS define visible rows. EF query filters are **not** applied. Admission
must authorize both route and credential/RLS context; a route parameter alone grants no
authority. Metadata-only readers need schema USAGE and SELECT on destination, dispatched_at,
dead_lettered_at, available_at, lease_until and queued_at. Payload/identity SELECT and lifecycle
UPDATE are unnecessary. Existing dispatch credentials already read these columns. There is
no schema migration, additional index, permission reconciliation or grant expansion.

## Native sample health and metrics

[OutboxDemo](../tests/Fixtures/OutboxHost/README.md#backlog-and-health) supplies editable consumer
wiring. Its authenticated `/operations/outbox/status` fixes exports.render and returns a live
snapshot with no-store caching. Database errors, including EF-wrapped native connection faults,
return 503 with no error details. Without its configured operator credential the route is absent.

Native `/health/live` reports process liveness. `/health/ready` reads storage afresh and checks
that the expected dispatch BackgroundService's execution task is running when `--worker` is
selected. Faulted, cancelled, completed or missing workers are unready. An operator-only host
expects no worker. Responses contain only native Healthy/Unhealthy status. The sample requests
a three-second health-check deadline. Delayed work, idle polling and dead letters do not
themselves fail readiness; thresholds remain consumer policy. A running task does not prove
transport availability, timely publication or business progress.

The dedicated host has one expected dispatch BackgroundService; its sampler is a separate
native IHostedService. A larger host must explicitly identify its expected worker set rather
than copying that topology assumption. Native StopHost usually shuts down on a worker fault;
under Ignore the process can stay live while readiness reports the stopped worker. Neither
health checks nor sampling restart workers or retry messages.

The sample's IMeterFactory-owned meter is **ModulithFoundry.OutboxDemo.Outbox**:

| Instrument | Unit |
| --- | --- |
| exports.outbox.pending | {message} |
| exports.outbox.eligible | {message} |
| exports.outbox.delayed | {message} |
| exports.outbox.leased | {message} |
| exports.outbox.dead_letters | {message} |
| exports.outbox.oldest_eligible.age | s |
| exports.outbox.observation.age | s |

These observable gauges have no message, tenant or route dimensions. A sampler reads every
five seconds after the preceding read, with a requested three-second query deadline. Positive
OUTBOX_DEMO_OBSERVATION_SECONDS changes the interval within native Task.Delay bounds.
Callbacks read a cached immutable successful observation and perform no database I/O. Before
the first success, every gauge is absent, meaning unknown. OldestEligibleAge is absent when
no row is eligible. Query faults log a warning and retain prior values; observation.age uses
monotonic elapsed time since that successful query **started**, including its latency, so old
values remain visibly stale. The next successful read replaces the observation. Readiness
independently queries current storage and does not trust that cache.

Individual instrument callbacks can straddle observations; the live snapshot API supplies
the coherent count partition. Exporters can retain a prior value after an instrument becomes
absent, so interpret eligible count and observation age together. Multiple host replicas may
observe the same backlog: summing their gauges double-counts that work. Select an observer or
use an appropriate per-instance/max policy. Sampling frequency and aggregate-query cost need
consumer workload assessment; this slice supplies no capacity benchmark.

Subscribe through a native MeterListener or the host's selected OpenTelemetry SDK:

```csharp
services.AddOpenTelemetry().WithMetrics(metrics =>
    metrics.AddMeter("ModulithFoundry.OutboxDemo.Outbox"));
```

Host SDK/exporter dependencies and resource identity stay explicit. These consumer instruments
are separate from the [Gloaming.Messaging attempt meter](observability.md#metrics). Their native
listener proofs do not certify a particular dashboard or exporter for the new gauges.

Consumer alert examples include a stale observation older than the chosen sampling/deadline
tolerance, eligible work exceeding an age threshold, and dead letters requiring operator review.
Choose actual values, aggregation and escalation for the owning workload. Pending delayed work
alone need not indicate an outage. Health responses, alert delivery, dashboards and deployment
restart policy are consumer responsibilities.

[Restricted Sales inspection](../../../samples/Wholesale/Worker/Broker/sales-outbox-dispatch.md#backlog-inspection)
uses the same callable read for three fixed command routes without opening a broker. The
[slice report](../../../docs/reports/outbox-operational-visibility.md) distinguishes new
PostgreSQL/native-metrics/HTTP/process evidence from earlier attempt telemetry and recovery
proofs. Inbox backlog, automatic alert delivery, durable telemetry, payload editing, replay,
inbox retention and lease renewal remain separate capabilities. Separately authorized
[completed-outbox retention](outbox-retention.md) is now available; it does not change these
backlog/worker observations or grant maintenance authority to this reader.
