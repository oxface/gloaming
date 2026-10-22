# Durable message observability

Messaging retains optional W3C diagnostic context and instruments selected PostgreSQL
dispatch/processing attempts using native .NET APIs. It requires no OTel SDK/exporter,
Aspire host, broker, tenancy or actor library. Collection is host configuration.

## Capture and schema upgrade

Choose the producer activity explicitly where the consumer maps its outgoing message:

```csharp
outbox.Enqueue(OutgoingMessage.FromPayload(
    messageId, routeKey, messageName, schemaVersion, payload, serializerOptions,
    correlationId: conversationId, causationId: incomingMessageId,
    traceParent: Activity.Current?.Id,
    traceState: Activity.Current?.TraceStateString));
```

This call stages work in the caller's native transaction. SaveChanges and commit remain
explicit. The library never reads Activity.Current to populate an envelope. An incoming
adapter similarly supplies received `traceParent`/`traceState`; these identify its received
upstream context, which can differ from the producer's original context after transport
publication. MessageId remains delivery identity; correlation and causation remain business
metadata. None of these diagnostic fields establishes tenant/producer/actor admission.

Both provided records retain nullable `trace_parent` and `trace_state` text columns.
Generate/apply a consumer-owned additive migration before running the updated model against
an existing database. Historical rows get null context and remain executable. Library
registration does not create tables or perform migrations. Pending outgoing-envelope
validation includes the captured context, protecting it from replacement before save.

Inbox deduplication compares existing delivery identity and business content, **excluding
diagnostic context**. Publication retries can carry different send spans. The first committed
intake retains its context; equivalent redelivery does not overwrite it. Changed payload,
contract, tenant, correlation or cause still conflicts under the existing rules.

Raw diagnostic strings are retained without rejecting otherwise valid work. Native
`ActivityContext.TryParse` creates links only for a valid parent. Missing/malformed parents
produce no link; tracestate remains opaque to that native API. The adapter should ignore
wrong diagnostic header types and apply its own size limits. Business metadata admission
can remain strict. There is no baggage or automatic identity propagation.

## Activities and logs

`TraceParent` is the W3C `version-trace-id-span-id-flags` string. A trace ID alone can
group/search records, but cannot identify the particular producer span or carry its sampling
flags. Retaining the full context lets a later worker link to that span after the producer
process has exited. This is a diagnostic relationship, independent of business correlation.

`TraceState` is optional, opaque vendor-specific tracing metadata, such as information used
by a vendor's sampler. It is normally absent in this sample. We retain it with its parent
context rather than invent our own tracing protocol. A link retains that upstream state;
a fresh worker root does not automatically inherit it as its own state. Neither field is
needed for successful delivery. See the [W3C definition](https://www.w3.org/TR/trace-context/).

Subscribe to `ActivitySource` **`Gloaming.Messaging`**:

| Activity | Kind | Boundary |
| --- | --- | --- |
| `gloaming.outbox.dispatch` | Internal | After a committed claim: publication through observed fenced completion, or failure/cancellation. |
| `gloaming.inbox.process` | Consumer | After locked selection: handler, save, completion and observed commit; failure includes savepoint rollback and failure accounting/dead-letter state. |

A link to retained context is supplied at activity creation, so samplers can inspect it.
Outbox publisher-exception accounting and consumer dead-letter decisions remain failed attempts;
they never report Published. Unconfirmed accounting faults emit the worker's distinct critical
supervision diagnostic. OutboxCompletionException likewise emits a distinct critical worker
diagnostic and faults that worker without another poll. Completion failures remain ambiguous
and do not invoke publication policy; completion-stage requested cancellation reports cancelled
while preserving the typed fault for direct callers;
see [outbox recovery](outbox-recovery.md). No new telemetry instrument or sensitive payload attribute is added.
Each retry receives its own activity; an existing local ambient parent is preserved.
NoWork polls and failures before selection do not create a message activity. Native DB
instrumentation can independently cover claim/selection.

Attributes are `messaging.message.id`, optional `gloaming.messaging.correlation_id` and
`gloaming.messaging.causation_id`, `gloaming.messaging.result`, and `error.type` on failure.
No payload or credentials are added as attributes. Published/Processed are reported only
after observed completion/commit, ClaimLost reports ambiguous recorded acceptance, and
requested cancellation is distinct from failure. Uncertain external or commit outcomes
retain the storage protocol's existing guarantees; a span does not resolve uncertainty.

Callable failure logging uses optional native ILogger dependencies while the attempt
activity remains current. DI uses the host's logger if registered. Direct outbox construction
can supply its optional fourth constructor parameter; the original three-argument call
still works. Hosted workers also log supervision failures outside the callable activity.
Consumer exceptions may contain sensitive detail: host log filtering/retention remain
consumer policy. No logging services become mandatory.

Trace/metric attempt tracking resides in the package-free messaging core and uses only
System.Diagnostics/Metrics. Its internal implementation is available to the PostgreSQL
assembly through explicit friend access; it adds no public telemetry API or core package
dependency. The dispatch/processing implementations determine boundaries and emit their
own generated failure logs before the attempt ends. These are operation failure logs
(including publisher or handler exceptions), not PostgreSQL engine diagnostics. Database
tracing/logging comes separately from native Npgsql/EF instrumentation. EF hosted workers
retain their existing supervision logs.

## Metrics

Subscribe to native meter **`Gloaming.Messaging`**:

| Instrument | Type/unit | Meaning |
| --- | --- | --- |
| `gloaming.messaging.attempts` | Counter / `{attempt}` | One measurement per completed selected attempt, including errors/cancellation. |
| `gloaming.messaging.attempt.duration` | Histogram / seconds | Time from selected-attempt instrumentation to disposal, including failure handling. |

The only dimensions are `operation` (`dispatch`, `process`) and `result` (`published`,
`claim_lost`, `processed`, `failed`, `cancelled`). NoWork polls are excluded. Attempt metrics
do not depend on trace sampling; process termination before disposal cannot emit a finished
attempt. They do not count every committed lease, broker delivery or business effect.
No per-message/tenant/route labels, backlog gauges or alert policy are supplied by this meter.
The [outbox snapshot and native consumer example](outbox-observability.md) now provide a separate
callable read, sample-owned backlog gauges and worker/storage readiness. Those instruments
use their own consumer meter and explicitly expose observation age.

## Native host and transport wiring

Subscribe in the host using the native OpenTelemetry builders. The activity source and meter
are both named `Gloaming.Messaging`; no Gloaming subscription package is needed:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// The host's editable Aspire-style ServiceDefaults configures native logs,
// HTTP/runtime/database instrumentation and configured OTLP export.
builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Gloaming.Messaging")
        .AddSource("RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber"))
    .WithMetrics(metrics => metrics.AddMeter("Gloaming.Messaging"));
```

Trace and metric collection can be selected independently. The technical packages emit
native .NET diagnostics and have no OpenTelemetry dependency. Hosts own the SDK and
subscription configuration; the documented source and meter names are supported contracts.

Select SDK/exporter packages, resources, sampling and OTLP export in the host. A producer
can add ASP.NET Core instrumentation; DB instrumentation stays independently selectable.
Use distinct service names/instances for API and worker roles. Without an interested activity
listener, no attempt activity may be allocated; messages still execute. Unsampled upstream
context can still be linked. Gloaming does not require a recorded flag or successful export.
ServiceDefaults is editable host source, not a Gloaming requirement or a mandatory Aspire
AppHost. Other hosts can subscribe with their own native OTel setup. The sample executables
reuse the existing sample ServiceDefaults project and add only role resources and messaging
source/meter subscriptions in their Program files.

The sample RabbitMQ adapters choose the active local publication context as one coherent
parent/state pair, falling back to retained context when no activity exists. The pinned client's
native instrumentation can replace the parent with its child send context. The send span is a child of
dispatch; incoming retention therefore points to that send span. The original envelope remains unchanged. This is why duplicate
business delivery must tolerate changing tracing. No process-global propagator override is
needed. Other transports are consumer-owned adapters, not guarantees of this slice.

## Using an OpenTelemetry sink

In an [Aspire dashboard](https://aspire.dev/dashboard/overview/), or another OTLP sink:

- **Traces:** look for `gloaming.outbox.dispatch` and `gloaming.inbox.process` under the
  corresponding worker service. Inspect message/correlation/cause attributes, result, error
  status and duration. A dispatch includes native RabbitMQ send and database child spans
  when those sources are enabled. Follow span links to the originating producer/send span.
  Delayed attempts can be separate traces, rather than one long HTTP request waterfall;
  each retry has a distinct span. The upstream trace must also have been collected and
  retained to inspect its details.
- **Structured logs:** a callable attempt failure includes MessageId/Operation and the
  current trace/span identifiers. Open its associated trace to see which operation failed.
  Worker supervision logs outside an attempt need not have that context.
- **Metrics:** select `Gloaming.Messaging` and the attempt counter/duration histogram,
  split by `operation` and `result`. These reveal attempt volume, failures and time spent
  dispatching/processing. They do not reveal queue backlog or end-to-end delivery latency.

These are ordinary native .NET signals. `MessagingTelemetry` is an internal core
trace/metric helper covering callable dispatch and processing, not a sink, worker, SDK or public facade.
It does not create a separate inbox intake span; the sample enables native transport and
database instrumentation around that lane. Dashboard navigation is documented behavior;
the automated process proof verifies actual OTLP data with a Collector, not dashboard UI.

See [the separate host example](../samples/Delivery/README.md) for executable
OTLP configuration. Its tests use real PostgreSQL/RabbitMQ and an official test Collector,
including process exit, completion/handler failure, new send spans, duplicate intake and
unavailable collection. [The OBS1 report](../../../docs/reports/obs1-durable-message-observability.md)
separates fresh results from earlier worker/recovery evidence.

The [worker recovery/shutdown continuation](worker-recovery.md) adds consumer publication-start
diagnostics and optional RabbitMQ integration diagnostics for native blocked/unblocked state
and terminal session loss. These
do not infer transport rejection or introduce metrics/accounting for every disconnect.
The existing unreachable-collector process scenario now separately budgets all three worker
stops at fifteen seconds with its existing one-second exporter timeout. That selected result
is distinct from a promise to flush arbitrary exporters or bound arbitrary publisher code;
see the [closure report](../../../docs/reports/w1-broker-recovery-shutdown.md).

Outbox backlog/oldest-age reads and consumer health/gauges are supported as described above.
Inbox backlog, automatic alerts, dashboard inspection, durable telemetry, automatic
tenant/actor propagation and deployment supervision/scaling remain deferred. Typed accounting/
completion faults already use native host supervision as described above. OTLP export can
target an Aspire dashboard, but Collector tests are not dashboard UX evidence. Publication
remains at least once; no exactly-once or new shutdown-deadline guarantee is introduced.
