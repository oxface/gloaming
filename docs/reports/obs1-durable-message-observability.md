# OBS1: durable message observability

Follow-up, 2026-10-22: the [worker recovery/shutdown closure](w1-broker-recovery-shutdown.md)
reuses the unavailable-collector scenario with a separate worker-stop budget and adds native
stalled-publication evidence. The counts and executions below remain historical OBS1 results.

Date: 2026-10-10. Interface/scope and implementation owner-approved;
checkpoint `b44b5d5` on `feat/durable-message-observability`, based on merged W1 `94ca57a`.
The owner subsequently pushed and merged the branch into `origin/main` at `4c0e0c5`.
Verification below records this slice's evidence; the status update adds no new runtime proof.

## Outcome

Incoming/outgoing envelopes and provided inbox/outbox rows retain explicitly supplied
optional W3C TraceParent/TraceState. Fresh dispatch/processing attempts use native .NET
activities with retained links at creation, native bounded attempt metrics and optional
failure logging while the activity remains current. SDK/exporter configuration stays in
editable hosts. The producer never gains a broker/receiver/worker reference.

The owner-approved optional `Gloaming.Messaging.OpenTelemetry` adapter supplies two native
AddGloamingMessaging overloads, for tracing and metrics independently. It depends only on
OpenTelemetry.Api and configures no provider, exporter, sampler, resource, logging or
transport. Both executable hosts use it alongside the existing editable ServiceDefaults.

Inbox duplicate comparison excludes diagnostic context and retains the first committed
intake's values. Different publication send spans can therefore represent the same business
delivery without conflict. Immutable pending outgoing validation includes diagnostic fields.
Additive migrations preserve historical pending messages with null context. Native adapters prefer a coherent local publication parent/state pair, without replacing the
retained envelope context. No domain rule,
business contract, audit field or transaction/lease/lock protocol changes.

New reusable mechanisms were proven: durable diagnostic retention integrated into existing
native operations, and shared callable-path activity/metric/log instrumentation. They remove
per-consumer reconstruction of links and outcome instrumentation. No new hosting, transport,
business workflow or telemetry framework was extracted.

## Review-worthy implementation

- [Envelope surface](../../src/Gloaming.Messaging/Gloaming.Messaging/OutgoingMessage.cs) and
  [incoming metadata](../../src/Gloaming.Messaging/Gloaming.Messaging/IncomingMessage.cs):
  optional raw W3C fields, typed-factory forwarding and XML documentation; no ambient capture.
- [Internal native trace/metric tracking](../../src/Gloaming.Messaging/Gloaming.Messaging/MessagingTelemetry.cs),
  [dispatch](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs)
  and [processing](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxProcessor.cs):
  selected-attempt boundaries, outcomes, links at sampling, optional native logger and bounded
  meter dimensions. BCL-only tracking resides in core; PostgreSQL uses internal friend
  access and owns its operation failure logs. NoWork polls have no message activity/attempt measurement.
- [Native intake](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInbox.cs),
  shared storage, row mappings and guards: retain diagnostics without adding them to business
  duplicate comparison. First committed intake wins, including competing writers.
- [Producer setup](../../samples/MessagingProducerDemo/Program.cs) and
  [worker setup](../../samples/MessagingWorkerDemo/Program.cs): reuse existing editable
  [ServiceDefaults](../../samples/Wholesale/ServiceDefaults/README.md), adding native role
  resources and source/meter subscriptions. No shared Gloaming host package or duplicate
  AddTelemetry helper. CI Messaging includes ServiceDefaults as a dependent source input.
- [Optional trace/meter subscription adapter](../../src/Gloaming.Messaging/Gloaming.Messaging.OpenTelemetry/README.md):
  two XML-documented extensions and one API-only dependency. The technical messaging packages
  retain their existing dependencies and need not adopt the adapter.
- [Real process/export proof](../../samples/MessagingWorkerDemo.Tests/ObservabilityTests.cs)
  and [Collector harness](../../samples/MessagingWorkerDemo.Tests/OtlpCollector.cs): actual
  OTLP ingestion/export, native transport spans, failures/restart and duplicate publication.
- Additive migration/designer/snapshot changes for Exports, Rendering and Inventory;
  [standalone sender upgrade](../../samples/OutboxDemo.Tests/TraceMigrationTests.cs),
  [receiver upgrade](../../samples/InboxDemo.Tests/TraceMigrationTests.cs) and
  [Inventory upgrade/adoption](../../samples/Wholesale/EventPersistenceDemo.Tests/InboxDispatchTests.cs).
- [Self-sufficient library contract](../../src/Gloaming.Messaging/docs/observability.md),
  [executable setup](../../samples/MessagingWorkerDemo/README.md),
  [approved scope/file map](../plans/durable-message-observability.md) and
  [ADR 0012](../adr/0012-durable-diagnostic-context-and-native-telemetry.md).

The architecture dependency assertion now includes the explicitly used, already-pinned
Logging.Abstractions package in PostgreSQL. The test-only native listener helper is shared
by the two family PostgreSQL suites. Inventory additionally uses the already-pinned native
in-memory exporter; this is a test dependency, not a library dependency. No package version
bump, CI job, template preset or archive edit. One new optional library project is included
in the solution, with the previously transitive OpenTelemetry.Api version explicitly pinned.
The existing CI path
map adds ServiceDefaults source to the Messaging lane because its executables now reuse it.

## Fresh verification

All counts below are new executions for this work, not copied W1/O1/I1/archive evidence.
Environment: .NET SDK 10.0.401/runtime 10.0.12, real PostgreSQL 18.6 and RabbitMQ 4.3.6
through rootless Podman. Real OTLP uses official Collector 0.162.0 and pinned host OTel
packages. Native in-memory export is a separate Inventory adoption assertion.

| Suite/check | Result |
| --- | --- |
| Provider-free MessagingTests | 8 passed. Explicit/omitted/invalid diagnostic metadata and typed-factory forwarding. |
| OutboxPostgresTests | 36 passed. Fresh retained links, sampler visibility, publication failure/recovery, correlated logs, ClaimLost/cancellation, metrics when tracing is dropped, immutable outgoing metadata and existing leases/transactions. |
| InboxPostgresTests | 34 passed. Competing equivalent intake with different context, first-winner retention, conflicting business content, processing rollback/fresh recovery, failure/cancellation telemetry and malformed/unrecorded context. |
| OutboxDemo.Tests | 6 passed. State-dependent commands and populated historical outbox upgrade followed by fresh dispatch without trace context. |
| InboxDemo.Tests | 13 passed. Populated historical inbox upgrade/fresh processing, wrong-type diagnostic broker headers, coherent local send/vendor-state propagation and existing native commit/ack/dedup cases. |
| MessagingWorkerDemo.Tests | 8 passed, 87.0 seconds including fixture lifecycle after the final layering refinement. Existing six W1 lifecycle cases plus two actual export/unavailable-collector cases. |
| EventPersistenceDemo.Tests | 190 passed in the full suite; the final preserved historical fixture/in-memory refinements additionally passed all 15 InboxDispatchTests. |
| ArchitectureTests | 80 passed after helper adoption. Typed module isolation, independent packages/adopters, optional adapter API-only dependency and permitted direct dependencies. |
| Active solution build | Passed, zero warnings/errors. |
| Native style/analyzer verification | Passed for the active solution. |
| CSharpier including generated code | Passed, 561 active files after removing the two AddTelemetry helpers and adding the optional subscription adapter. |
| Git/whitespace integrity | Clean diff check; staged review snapshot preserved. Frozen archive untouched. |

The process/export oracle checks actual service/trace/span IDs and links, not merely their
presence. It observes producer exit before dispatch and intake exit before processing.
A database trigger fails completion **after broker acceptance**, leaving the actual lease
to expire; fresh dispatch republishes with a new send context. Equivalent intake remains
AlreadyReceived and preserves its first context. Another trigger fails transactional job
handling; fresh processing commits exactly one job. Exported failure logs match attempt
trace/span IDs, and exported counters/histograms identify the expected outcomes.

Collector unavailability leaves independent producer commit, publication, retained intake
and local job processing functional. This proves business independence from collection,
not a new maximum shutdown duration or guaranteed diagnostic delivery after process death.
The six W1 termination/competition cases were rerun with this host setup, but remain the
storage/lifetime proof rather than newly invented tracing guarantees.

The owner-requested ServiceDefaults replacement was verified by rerunning all eight process
tests and 79 architecture tests, rebuilding the active solution and checking style, analyzers
and formatting. The shared project's implementation is unchanged; the hosts now exercise
its existing HTTP/runtime/database instrumentation and OTLP configuration alongside their
own native messaging subscriptions. This proves actual collection with ServiceDefaults;
Aspire dashboard UI navigation was not added to the automated proof.

The follow-up optional helpers are owner-reviewed interface additions. Their adoption is
verified by the same executable process/export proof rather than tests mirroring their
AddSource/AddMeter forwarding. Architecture coverage additionally checks that the adapter
declares only OpenTelemetry.Api and no project/framework reference. The follow-up rerun
passed all eight process/export tests and 80 architecture tests, plus solution build,
style, analyzers and formatting.
Its actual restored runtime dependency graph also contains only OpenTelemetry.Api 1.19.1;
there is no SDK or exporter transitive dependency on .NET 10. Generated XML documentation
includes both public extension methods and their host-owned configuration limits.

The owner-requested layering refinement places internal native trace/metric tracking in
Gloaming.Messaging and generated failure logging beside each callable PostgreSQL operation.
The core grants internal friend access to the PostgreSQL assembly without exposing a new
public API or adding a package dependency; its restored dependency graph remains empty.
Operation failures include publisher/handler errors and are distinct from native database
diagnostics. Logging still happens before the activity ends, preserving trace/span correlation.
This is organization of the existing mechanism, not a new delivery or telemetry guarantee.
After this refinement, 166 tests passed: core 8, outbox PostgreSQL 36, inbox PostgreSQL 34,
process/export 8 and architecture 80. The existing failure-log assertions and actual
exported log/span correlation remain valid. Solution build, style, analyzers, formatting,
documentation links and diff checks passed; the staged review snapshot was unchanged.

## Migration evidence and limits

Merged migrations are unchanged. The three new migrations only add nullable trace columns;
snapshots track those additions. Historical-schema fixtures insert pending rows natively
before upgrade rather than run today's changed EF model against missing columns.

Inventory preserves the original real stock decision, three facts/current state, outgoing
reply and pending intake assertions. Its historical outbox fixture captures the real command's
envelope and inserts it into the old schema inside the same native transaction. After forward
migration, normal current inbox processing adds its expected stock change, audit and reply.
This fixture tests preservation/recovery, not backward runtime compatibility with an unmigrated
schema. Standalone sender/receiver tests also execute historical pending messages after upgrade.

## Mechanism versus consumer policy

Library mechanisms: optional retention/guards, native reconstructed links, observed callable
outcomes, optional correlated failure logging and fixed bounded attempt metrics. Core is still
BCL-only; EF acquires no provider/transport dependency; PostgreSQL acquires no OTel SDK or
RabbitMQ dependency. No listener/exporter is required for business execution.

Consumer policy: which activity to capture; transport/header format and limits; producer/tenant
admission; business correlation/causation and idempotency; transactions on producer/intake;
publisher acceptance/acknowledgement; native host subscriptions, sampling, resources/export,
log filtering/retention and supervision. Exports is state-stored and tenantless; Inventory is
event-sourced with admitted tenant/actor scope and an explicit reply. Neither domain travels
into the library. Audit continues to exclude messaging/diagnostic fields.

Templates: no new option or generated runtime. Existing T1 event/messaging-free adoption remains
unchanged. Editable native host setup is exercised sample code, not template promotion.

## Remaining gaps and historical evidence

Publication remains at least once. Span status does not establish exactly-once delivery,
business idempotency or certainty after an ambiguous commit/acceptance response. Malformed
parent context yields no link; tracestate is opaque to the native parser. Metrics count finished
selected attempts, not every committed claim, and do not survive hard process death before disposal.

Backlog/oldest-age monitoring, alerts, dashboard UX proof, durable telemetry, baggage,
automatic tenant/actor propagation, worker scaling/supervision, queued compatibility/redrive,
workflows and repopulation remain separately planned. The configured OTLP endpoint can be an
Aspire dashboard; these Collector tests are not a dashboard inspection.

Pinned RabbitMQ SourceLink and native .NET inspection informed propagation/sampling choices.
Archived metrics and TraceId-only logging are comparison evidence only; no archive tests ran.
The W1 and earlier messaging reports retain separately dated historical proofs. This report
does not promote untested transports, SDK configurations, providers or deployment topologies.
