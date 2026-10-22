# OBS1: durable message observability

Status: owner-approved and checkpointed as `b44b5d5`, 2026-10-10;
merged into `origin/main` at `4c0e0c5`.
Base: merged W1 at `94ca57a`, on `feat/durable-message-observability` without an upstream.
Fresh implementation evidence is in [the OBS1 report](../reports/obs1-durable-message-observability.md).
The approved proposal below records the boundary reviewed before implementation.

## Bounded outcome and callers inspected before implementation

Instrument the existing Exports → Rendering journey across separate producer, dispatch,
intake and processing processes. Follow work after the earlier process has exited,
including publication retries and processing failures. Reuse W1's executable topology.

- `ExportRequestCommands.SubmitAsync` loads tracked state, checks version/eligibility,
  changes the request and calls `OutgoingMessage.FromPayload`. The HTTP caller saves and
  commits. The envelope currently has no trace context.
- `PostgresOutboxDispatcher.DispatchNextAsync` claims in its own transaction, publishes
  outside that transaction and fences completion in another. A fresh worker retains
  business IDs but cannot reconstruct a relationship to the producer span.
- `RabbitMqRenderReceiver` commits durable intake before acknowledgement. Later,
  `PostgresInboxProcessor.ProcessNextAsync` owns a separate local transaction through
  handling, SaveChanges, completion and commit.
- `StockIssueMessages` and the Inventory RabbitMQ adapters are a materially different,
  tenant-admitted event-sourced adopter. Its active mapped inbox/outbox also need migration
  when the provided row shape changes.
- Existing hosted workers log exceptions after the callable operation unwinds. Such a log
  does not automatically retain an activity that ended inside that operation.

The prior draft also proposed a new stock-issue audit/reply demonstration, dashboard
backlog queries and alerts. Those are independent follow-ups. This slice uses the existing
business journey; audit interfaces/records acquire no diagnostic fields.

## Owner-reviewed public interface

The approved additions append optional parameters to the existing constructors and factory:

```csharp
public IncomingMessage(
    Guid messageId, string producerKey, string messageName, int schemaVersion,
    JsonElement payload, string? tenantKey = null, string? correlationId = null,
    string? causationId = null, string? traceParent = null, string? traceState = null);

public OutgoingMessage(
    Guid messageId, string routeKey, string messageName, int schemaVersion,
    JsonElement payload, string? tenantKey = null, string? correlationId = null,
    string? causationId = null, string? traceParent = null, string? traceState = null);

public static OutgoingMessage FromPayload<TPayload>(
    Guid messageId, string routeKey, string messageName, int schemaVersion,
    TPayload payload, JsonSerializerOptions serializerOptions,
    string? tenantKey = null, string? correlationId = null,
    string? causationId = null, string? traceParent = null, string? traceState = null);

// Both envelopes; also readable on both provided durable records.
public string? TraceParent { get; }
public string? TraceState { get; }
```

Outgoing context identifies the producer activity selected when staging. Incoming context
identifies the received upstream activity selected by the transport adapter. Both are
diagnostic relationships, separate from delivery/conversation/cause identity and admission.
The library does not implicitly capture Activity.Current or initialize business IDs.
Raw W3C strings match transport/persistence without a new Gloaming context type or generic
argument flowing through dispatch/handlers.

Add an optional native logger to the existing callable dispatch constructor:

```csharp
public PostgresOutboxDispatcher(
    TDbContext database, IMessagePublisher publisher, OutboxDispatchOptions options,
    ILogger<PostgresOutboxDispatcher<TDbContext>>? logger = null);
```

DI supplies a logger when configured; direct three-argument construction still works.
The internal inbox processor likewise receives an optional native logger. Message-attempt
failure logs are emitted before the operation activity ends, including failures after the
publisher/handler returns. Existing hosted-worker supervision logs still cover failures
before selection. No logging service becomes mandatory.

Existing callers remain source-compatible, but compiled consumers must rebuild; this is
not a binary-compatibility promise for unreleased assemblies. No public dispatcher,
processor, worker, publisher or handler interface changes beyond these additions.

## Concrete consumer usage

Inside the existing accepted command, with the caller's native transaction already open:

```csharp
outbox.Enqueue(OutgoingMessage.FromPayload(
    Guid.NewGuid(), "exports.render", "exports.render", 1,
    new RenderExportV1(request.Id, request.Pages), WireJson,
    traceParent: Activity.Current?.Id,
    traceState: Activity.Current?.TraceStateString));

// The consumer still performs these operations.
await database.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The consumer chooses its HTTP/command activity. Optional native creation spans can refine
that choice without changing the library interface. Existing callers supplying neither
value remain valid. Incoming adapters decode diagnostic headers tolerantly; business
admission stays strict. Inventory still establishes tenant/actor scopes itself.

Editable native host configuration subscribes to the sources and meter:

```csharp
builder.AddServiceDefaults(); // Editable native host wiring.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddGloamingMessaging()
        .AddSource("RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber"))
    .WithMetrics(metrics => metrics.AddGloamingMessaging());
```

The hosts reuse the existing editable sample ServiceDefaults for logging, HTTP/runtime/DB
instrumentation and configured OTLP export. Program files add messaging subscriptions and
resources. API, dispatch, receive and process roles have distinct
service names/instances. The producer gains no broker/receiver/worker-project reference.
No template preset or Gloaming host configuration framework is added.

## Follow-up: optional native OTel subscriptions

Owner requested convenience extensions after reviewing native ServiceDefaults adoption,
then explicitly approved this optional package and both overloads before implementation.
They are implemented for final owner code review:

```csharp
// Namespace OpenTelemetry.Trace
public static class GloamingMessagingTracerProviderBuilderExtensions
{
    public static TracerProviderBuilder AddGloamingMessaging(this TracerProviderBuilder builder);
}

// Namespace OpenTelemetry.Metrics
public static class GloamingMessagingMeterProviderBuilderExtensions
{
    public static MeterProviderBuilder AddGloamingMessaging(this MeterProviderBuilder builder);
}
```

Both return the supplied native builder for normal chaining. They respectively subscribe
to the existing `Gloaming.Messaging` activity source and meter. Null builders throw
ArgumentNullException. They do not construct a provider, alter resources/sampling, enable
export, select transport instrumentation or change logging. Each can be used independently.
Scopes, transactions, retries, process shutdown and delivery guarantees are unchanged.

They reside in independently optional `Gloaming.Messaging.OpenTelemetry`, with the sole direct
package dependency `OpenTelemetry.Api` 1.19.1 (already present transitively in the pinned host
stack; now explicitly centrally pinned). Native TracerProviderBuilder.AddSource and
MeterProviderBuilder.AddMeter are declared in that API assembly, so no SDK, EF, PostgreSQL,
RabbitMQ, hosting or other Gloaming project dependency is needed. Existing technical
packages retain their dependency boundaries. This package hides repeated instrumentation
names in the two actual hosts without becoming a host configuration facade.

Consumer usage:

```csharp
builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddGloamingMessaging()
        .AddSource("RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber"))
    .WithMetrics(metrics => metrics.AddGloamingMessaging());
```

Exact additional scope: new project/README and two XML-documented extension files under
`src/Gloaming.Messaging/Gloaming.Messaging.OpenTelemetry/`; explicit central API pin;
solution library entry; project references and registration replacements in both messaging
hosts; an independent-dependency assertion in ArchitectureTests; native helper usage in the
family docs/READMEs, ADR 0012 and this slice report. The existing Messaging CI lane already
covers the family path and executable worker suite; no additional test project or CI job.
Rerun the real process/OTLP tests to prove collection through these extensions, architecture
checks to prove optional dependency isolation, and relevant build/format/analyzer checks.
No archival evidence establishes this new helper; its proof comes from current adopters.

## Retention, errors and guarantees

- Add nullable `trace_parent` / `trace_state` columns. Enqueue captures them; native claim
  and read reconstruct them. Immutable pending-envelope validation includes both values.
- Inbox deduplication compares the existing key and business envelope, **excluding trace
  context**. Equivalent retries can carry different send spans. Preserve the first
  committed intake's context; changed business content remains a conflict.
- Envelopes accept diagnostic strings without making them business validation. Native
  `ActivityContext.TryParse` supplies links; absent/invalid parents produce no link.
  Tracestate is opaque to that API, not fully W3C-validated by Gloaming. Wrong diagnostic
  header types are ignored. Transport size limits and collection policy are adapter-owned.
- Create an activity per selected attempt, with a link to retained context supplied **at
  creation**. Preserve a local ambient parent when applicable rather than force every
  delayed retry under the old producer.
- `gloaming.outbox.dispatch` is an Internal operation covering publication and fenced
  completion after claim. `gloaming.inbox.process` is a Consumer operation covering
  handling and observed commit after selection. Claim acquisition/selection remain native
  DB operations outside these activities. NoWork polls produce no message activity.
- Published/Processed are recorded only after existing completion/commit returns.
  ClaimLost means external acceptance may have occurred without recorded completion.
  Exceptions remain exceptions; requested cancellation is distinct from failure.
  No transaction, lock/lease, retry or disposal ownership changes.
- Native meter `Gloaming.Messaging` provides `gloaming.messaging.attempts` and
  `gloaming.messaging.attempt.duration` (seconds). Count selected attempts by bounded
  `operation` and `result`, including failure/cancellation; exclude NoWork polls. Duration
  covers the same attempt boundary. IDs, routes, tenants, payloads and arbitrary exception
  text are not metric dimensions.
- Activities/logs can carry opaque message/correlation/causation IDs and outcome; exclude
  payloads/credentials. Hosts decide sampling, filtering and retention. No-listener and
  unsampled execution remain functional; metrics do not depend on trace sampling.

Publication remains at least once. Tracing does not establish exactly-once delivery,
business idempotency, commit in an ambiguous outcome or broker acknowledgement.
Collector unavailability must not decide business commit. A new shutdown deadline is
not guaranteed by this slice.

## Dependencies and ownership

Core remains BCL-only. EF retains model/guard responsibilities. PostgreSQL uses native
core System.Diagnostics/Metrics attempt tracking through internal friend access, and an explicit already-pinned
`Microsoft.Extensions.Logging.Abstractions` reference for its logger contract, without
an OTel SDK or RabbitMQ dependency. The operation classes own generated failure logs;
the core instrumentation has no ILogger dependency or new public API.

The two executable hosts reference the existing sample ServiceDefaults project, which owns
already-pinned OTel hosting/OTLP packages, and the independently optional API-only
Gloaming.Messaging.OpenTelemetry subscription adapter. Messaging-specific resource and
transport-source configuration stays in Program.cs. Tests use native listeners and
the pinned in-memory exporter. A test-owned official Collector container accepts actual
OTLP from child processes and writes signals for structural assertions. Pin its release
image in the harness (`0.162.0`, release reference below). It is proof infrastructure,
not a production runtime requirement or a new repository script.

Producer scope/context/transaction/staging/save/commit remain consumer-owned. Dispatch
owns its existing claim/completion transactions; the consumer publisher owns transport
acceptance. The receiver owns admission, intake transaction and acknowledgement. Processing
owns its existing local transaction and invokes the consumer handler. Hosts own connections,
DI scopes, worker lifetime, export and external restart policy. No ownership transfers.

## Exact file/behavior change map

Paths are repository-relative. New migration filenames acquire their generated timestamp;
the reviewed operation is explicitly adding nullable columns to populated databases.

| Files | Planned change |
| --- | --- |
| `src/Gloaming.Messaging/Gloaming.Messaging/{IncomingMessage,OutgoingMessage}.cs` | Optional parameters/properties, factory forwarding and XML docs. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/{InboxMessageRecord,OutboxMessageRecord}.cs` | Readable retained fields and outgoing capture. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/{InboxModelExtensions,OutboxModelExtensions}.cs` | Nullable mappings and outgoing immutable-envelope validation. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/{PostgresInbox,PostgresInboxStorage,PostgresOutboxDispatcher}.cs` | Insert/select/hydrate context, validate mapped columns; preserve duplicate business comparison. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/{PostgresInboxProcessor,PostgresInboxServiceCollectionExtensions,PostgresOutboxServiceCollectionExtensions}.cs` | Callable instrumentation and optional logger resolution. |
| New core `MessagingTelemetry.cs`; core `.csproj`; provider operation classes and `.csproj` | BCL-only internal activity/meter tracking, explicit friend access for the PostgreSQL assembly, and operation-owned generated failure logs with an explicit logging-abstractions reference. |
| New `src/Gloaming.Messaging/Gloaming.Messaging.OpenTelemetry/` project, README and two extension files; `Directory.Packages.props`; `ModulithFoundry.slnx` | Owner-approved optional trace/meter subscription helpers, sole OpenTelemetry.Api dependency and explicit version pin; add one library solution entry. |
| `src/Gloaming.Messaging/samples/OutboxDemo/ExportRequestCommands.cs`; `samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueMessages.cs` | Explicit consumer context capture; no business or audit changes. |
| `src/Gloaming.Messaging/samples/MessagingDemo/{RabbitMqExportPublisher,RabbitMqRenderReceiver}.cs`; `samples/Wholesale/EventPersistenceDemo/{InventoryRabbitMqPublisher,InventoryRabbitMqReceiver}.cs` | Pass diagnostic headers, tolerant diagnostic parsing. Native send instrumentation may replace context on each publication. |
| `src/Gloaming.Messaging/samples/MessagingProducerDemo/{Program.cs,MessagingProducerDemo.csproj}`; `src/Gloaming.Messaging/samples/MessagingWorkerDemo/{Program.cs,MessagingWorkerDemo.csproj}`; `samples/Wholesale/ServiceDefaults/README.md` | Reuse existing editable native ServiceDefaults; add messaging subscriptions and distinct role resources in Program.cs. Remove the proposed AddTelemetry helpers; no lifecycle changes. |
| `.github/ci-paths.yml` | Include shared ServiceDefaults source in the Messaging lane's dependent input paths. |
| `src/Gloaming.Messaging/samples/OutboxDemo/Migrations/`, `src/Gloaming.Messaging/samples/InboxDemo/Migrations/`, `samples/Wholesale/modules/Inventory/Inventory/Migrations/` | Add one migration/designer and update snapshot per context; preserve merged migrations. |
| `src/Gloaming.Messaging/tests/MessagingTests/MessageTests.cs`; new `TelemetryTests.cs` in each PostgreSQL suite | Envelope forwarding, retention/dedup and native activity/link/log/meter proofs on real PostgreSQL. |
| New `src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/{ObservabilityTests,OtlpCollector}.cs`; existing `{ChildHost,WorkerTopology}.cs` and test `.csproj` | Real process/export proofs and narrowly scoped telemetry configuration; keep existing W1 proofs. |
| `src/Gloaming.Messaging/samples/OutboxDemo.Tests/`, `src/Gloaming.Messaging/samples/InboxDemo.Tests/`, `samples/Wholesale/EventPersistenceDemo.Tests/` | Populated migration upgrades and native diagnostic/duplicate coverage in existing suites. |
| Messaging family/package READMEs, new `src/Gloaming.Messaging/docs/observability.md`, sample READMEs | Self-sufficient supported setup, limits and troubleshooting after implementation. |
| `tests/ArchitectureTests/{AdoptionDependencyTests.cs,ArchitectureTests.csproj}` | Admit the explicitly used, owner-reviewed logging-abstractions dependency; assert the optional subscription adapter depends only on OpenTelemetry.Api using its copied project declaration. Retain other package boundaries. |
| This plan, `docs/plans/{library-extraction,remaining-capability-roadmap}.md`, new `docs/reports/obs1-durable-message-observability.md` and `docs/adr/0012-durable-diagnostic-context-and-native-telemetry.md` | Reviewed status/decision and fresh evidence separated from W1/archive evidence. |

No relocation, archive edit, fixture reset, business-contract change or template runtime is
planned. Owner follow-up replaces the two proposed AddTelemetry files with existing
ServiceDefaults adoption and adds the reviewed optional subscription adapter. CI Messaging already runs the worker suite and now
also responds to ServiceDefaults source changes; existing consumer/EventSourcing and
repository lanes cover the other adopters. No workflow/job addition is required.

## Verification contract

1. Explicit context/factory forwarding and independent no-tracing adoption.
2. Real PostgreSQL fresh-context retention and outgoing mutation guards. Equivalent duplicate
   intake with changed context remains AlreadyReceived and preserves first intake; changed
   business content still conflicts, including competing intake.
3. Native listener/exporter assertions for links and outcomes: dispatch success/failure/
   ClaimLost, processing commit/failure/cancellation, correlated failure logs and measured
   counters/duration. Structural relationships, not merely nonempty IDs.
4. Real PostgreSQL/RabbitMQ child processes exporting OTLP to the test Collector. Producer
   exits before dispatch and intake before processing. Prove service identity, actual links/
   transport spans, stable business IDs and one accepted job through retry and restart.
   Changed send contexts must not conflict at intake.
5. Absent listeners, propagation-only/unsampled context, malformed tracing and unreachable
   collector leave business processing functional; no inferred shutdown-duration guarantee.
6. Upgrade populated Exports, Rendering and Inventory schemas. Historical pending rows have
   null context and still dispatch/process; merged migration history survives.
7. Relevant existing messaging/adopter/architecture checks, build/analyzers/formatting and
   unchanged owner index. Do not rerun frozen archive suites.

Archived native metrics/TraceId logging are comparison evidence, not fresh proofs. W1's
tests prove prior locks/leases/recovery, not new trace relationships. Pre-implementation research ran no runtime tests. The report records fresh verification
separately, including the final native listeners, in-memory exporter and real OTLP proofs.

## Explicitly deferred

Backlog/oldest-age observation, alerts, durable telemetry, dashboard UX proof, additional
audit/reply scenarios, baggage, automatic tenant/actor propagation, business workflow
engines and Gloaming-wide telemetry wrappers. Existing OTLP configuration can target an
Aspire dashboard, but Collector export tests do not prove a dashboard inspection. Planned
messaging operations, worker scaling and repopulation remain separate slices.

## Primary evidence checked for this proposal

- [RabbitMQ.Client 7.2.1 activity sources](https://github.com/rabbitmq/rabbitmq-dotnet-client/blob/v7.2.1/projects/RabbitMQ.Client/Impl/RabbitMQActivitySource.cs)
  and [publishing/header injection](https://github.com/rabbitmq/rabbitmq-dotnet-client/blob/v7.2.1/projects/RabbitMQ.Client/Impl/Channel.BasicPublish.cs).
  Matching SourceLink inspection verified source at commit
  `76a88bd02f099f24966c188d0cff6cb8b2f0bc70`. Injection writes the send activity's
  context; it does not provide stable retry diagnostic identity.
- [OTel messaging relationships](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/):
  links are recommended generally; messaging conventions remain Development. This is a
  documented adoption policy, not complete semantic-convention compliance.
- [.NET ActivitySource instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs):
  no interested listener can mean no activity. Matching .NET 10 inspection confirms native
  ActivityContext parsing; no required SDK or home-grown parser is needed.
- [Collector configuration](https://opentelemetry.io/docs/collector/configuration/),
  [file exporter](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/v0.162.0/exporter/fileexporter)
  and [pinned release](https://github.com/open-telemetry/opentelemetry-collector-contrib/releases/tag/v0.162.0)
  support the actual OTLP proof; fresh results are recorded in the report.
