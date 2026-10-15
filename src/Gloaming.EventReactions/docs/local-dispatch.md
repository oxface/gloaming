# Event capture and reaction lifecycle

The [core](../Gloaming.EventReactions/README.md) supplies capture, routing and bounded dispatch.
The [EF adapter](../Gloaming.EventReactions.EntityFrameworkCore/README.md) contributes preparation
to the [shared save integration](../../Gloaming.EntityFrameworkCore/README.md).

## Intended use and ownership

Use local reactions for bounded required work inside one owning transaction. All required reactions
finish before commit; failure aborts the operation. Prefer explicit application orchestration when
business steps, branching or intermediate results would otherwise be hidden across subscriptions.
The registry is routing, not a dependency graph, scheduler or saga engine. A ceiling catches runaway
execution without proving a cyclic business rule correct.

Objects, inheritance, event meaning, subscriptions, mappings and concurrency remain consumer-owned.
Events can be plain reference or value records. No marker, aggregate base or event-source interface
is required. Required sources are explicitly selected; do not infer business intent from EF row diffs.

## Capture and execution

One serialized operation owns its dispatcher, buffers and handlers. Registry bindings freeze on
construction and match exact CLR event types. Each Raise captures an occurrence using an internal
process-local sequence. The dispatcher deduplicates participating buffers, removes the collected
batch and orders it by capture sequence. Handler registration order is preserved within each event.
Equal payloads and repeated raises of the same object remain distinct.

All handlers in the current wave finish before collecting downstream events and newly exposed sources.
Handler-created events receive later capture positions and are ordered across their sources in the next
wave. Expose all current buffers before dispatch; older facts hidden until a later wave cannot be
retroactively placed before completed work. Repeated dispatch handles only new occurrences.

The sequence is not persisted, a domain version, wall-clock chronology or a cross-process order.
Interlocked allocation does not make the buffers thread safe. No parallel operation work is supported.
Capture order cannot infer a dependency such as completing B's decision before producing C's fact.

Any object can compose PendingEvents. The operation sink belongs to the dispatcher and is always
collected; it is not an aggregate buffer. PendingCount counts that sink only. Explicit source selectors
collect other buffers directly, without copying their events into the operation sink. The generic
tag selects routing and DI bindings rather than security or inter-module publication.

The EF adapter accepts one registration per context. Combine that context's sources and subscriptions
there, and use distinct tags for independent contexts. Additional registrations for the same context
are rejected during DI setup; composition of several tagged dispatchers for one context is unsupported.

## Persistence and failure contract

Both sync and async paths exist. Sync rejects async-only work without blocking. Cancellation is
cooperative; a ceiling cannot stop a hung handler. Nested dispatch and saving from capability execution
are rejected, including swallowed nested errors. Observed errors fault those objects. Native
persistence/commit errors have no universal reuse guard.

The EF caller begins the native transaction before pending work is handled, saves and commits after
SaveChanges returns. Gloaming never starts or commits it. Shared preparation finishes before change
detection and selected validators. External native overrides/events/interceptors keep their native order.
Validators should inspect the candidate rather than produce new decisions.

On every failure, roll back/dispose and recreate the operation from committed state. Cleared events
and mutated objects are not restored by rollback. Native SaveChanges(false) uses AcceptAllChanges
after success. Premature acceptance can discard producer writes while their events still cause reactions.
Successful saving and event consumption do not promise caller commit or in-memory rollback repair.

Persistence callbacks must not raise events; they can remain pending after success. Earlier suppression
is rejected for required capabilities; later suppression cannot be controlled. Direct SQL/bulk writes,
independent commits, omitted sources/configuration and automatic same-context retry bypass this protocol.
External effects need a separately justified durable mechanism.

## Separate capabilities

DDD models, deciders, business-history retention, event sourcing, messaging and durable workflows do
not require this package and are not supplied by it. Required intra-module writes remain atomic.
Deferred local work needs its own concrete requirement. D2.3 separately assesses durable in-process
module-contract delivery and shared transactions. No new template preset is provided.

The [consolidation report](../../../docs/reports/event-reactions-consolidation.md) supersedes the
former Gloaming.Domain positioning and source contract; previous reports retain dated evidence.
