# Outbox operational visibility

Status: owner selected the next slice on 2026-10-21 after approving completion supervision,
checkpoint `020a292`. The concrete surface below is implemented for owner line-by-line review.
The [slice report](../reports/outbox-operational-visibility.md) records fresh verification,
extraction findings and remaining limits.

## Scope and consumer surface

Add one callable PostgreSQL read, without registering a publisher, worker or recovery store:

```csharp
OutboxBacklogSnapshot snapshot = await database.ReadPostgresOutboxBacklogAsync(
    "exports.render", cancellationToken);
```

The extension requires a fresh independently owned context, validates the supported mapping
and parameterizes the exact consumer-admitted route. It owns a short ReadCommitted transaction
and one aggregate SELECT with a single database statement time. Native grants/RLS govern its
visible rows; raw SQL does not apply EF query filters. SELECT-only credentials need schema
USAGE and SELECT on destination, dispatched_at, dead_lettered_at, available_at, lease_until
and queued_at. No payload, identity or lease-token read is needed.

The EF-layer detached OutboxBacklogSnapshot contains ObservedAt, Pending, Eligible, Delayed,
Leased, DeadLetters and nullable OldestEligibleAge. Pending excludes completed/dead-lettered
work and equals Eligible + Delayed + Leased. An unexpired lease takes precedence over delay;
expired leases count as eligible or delayed according to availability. DeadLetters counts
undispatched stopped work separately. OldestEligibleAge measures database observation time
minus the earliest original QueuedAt among eligible rows, clamped at zero, and is absent
when none is eligible. It includes time spent in previous attempts/delays; it is not a
continuous-wait duration or end-to-end delivery latency.

Snapshots are live observations. They take no row locks or claims, may include work another
transaction currently locks, and cannot establish that a future dispatcher will find work.
SQL/permission/cancellation failures throw rather than fabricating an empty backlog. No
global queue framework, recovery interface, migration, grant expansion or transport dependency.

## Executable adoption

OutboxDemo fixes exports.render and adds:

- An authenticated `/operations/outbox/status` using the existing operator credential/permission.
- Native `/health/live` for process liveness and `/health/ready` for a fresh backlog read and,
  when configured, the expected dispatch BackgroundService's live execution task. Stopped,
  faulted, cancelled or missing workers fail readiness. An operator-only host expects no worker.
  Backlog thresholds and dead letters do not themselves make this sample unready.
- A consumer-owned five-second sampler and native meter `ModulithFoundry.OutboxDemo.Outbox`.
  Observable gauges read one cached successful snapshot without database I/O in callbacks.
  Observation-age seconds expose stale samples; absence before the first success means
  unknown. Query faults retain the prior observation and increase its age. Gauges have no
  message/tenant/route dimensions. Native exporter setup and alert thresholds remain consumer
  choices; readiness independently reads current storage.

The restricted Sales console adds an explicit inspection role using its existing dispatch
credential and three fixed command routes. It needs no broker to inspect. The separate native
health policy remains local to the HTTP adopter; no library health framework or worker-state
API is introduced. The sampler is a native IHostedService, leaving the dedicated host's one
dispatch BackgroundService directly observable without inspecting its internal type.

## Proof boundaries and file scope

Use the public PostgreSQL extension/mapped row, real native metrics listeners, authenticated
Kestrel endpoints/HealthCheckService, and built Sales/OutboxDemo process roles. Prove empty and
mixed state, exact-route isolation, one-clock partitions, expired leases, commit visibility,
nonblocking reads under row locks, SELECT-only/RLS authority and rejection of invalid contexts.
Prove authorization, missing schema/database, live versus ready, faulted worker under native
Ignore, normal idle/delayed work, cached metric staleness/recovery and restricted executable
inspection. Keep existing completion/accounting/fencing proofs as regressions.

Review the snapshot DTO and provider extension, sample health/status/sampler and Sales role,
then their PostgreSQL/HTTP/native-metrics/process tests. Update family setup/current limits,
design/extraction/roadmap and a dated slice report. No template preset, alert service,
dashboard, durable telemetry, capacity benchmark, retention or lease renewal is selected.
