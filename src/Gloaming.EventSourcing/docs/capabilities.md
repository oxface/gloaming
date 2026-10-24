# Current capabilities and deferred work

The supported model is append-only per-stream events plus one required transactionally updated
inline command aggregate. Domain evolution and event encoding are consumer mappings. Native EF
save validation enforces tracked header/event/state participation; the consumer must call it
from both native save overrides and own the transaction and final save/commit.

Full replay is a separate explicit maintenance lane. The rebuilder inserts missing state or
replaces state without decoding an old corrupt body, preserving retained facts/version/times.
Appends and rebuilds change a native header concurrency stamp. Same-version repair invalidates
stale decisions at save. This optimistic contract permits overlap and native conflicts; it
provides no pre-read exclusion. Dispose and reload after failure. Read access never saves repairs.
The EF adapter contains no Npgsql dependency; PostgreSQL is the runtime proved by family and
consumer integration tests. No portability certification or pessimistic provider seam is claimed.

Consumers can reconstruct live views on access with the provided bounded EF history reader and
their own decoders/reducers. The reader uses complete mapped keys, captured version bounds, native
filters and ordered range/UTC endpoint validation, without tracking or saving. The EF adapter
reuses Events.History integrity checks but has no serialization dependency. Temporal cutoff
selection and initial stream lookup remain consumer-owned. Rebuilders inject this independent
reader; writing requires no history registration. Native
Queries/Filters can filter and join stored aggregate state without persisting secondary views.
The optional [PostgreSQL feed module](../Gloaming.EventSourcing.EntityFrameworkCore.Postgres/README.md)
provides committed multi-stream reads, bounded transactional secondary projections and generation
backfill/cutover over explicitly installed sources. It adds a declared writer-serialization cost.
Secondary inline projection composition remains consumer-owned. Main aggregate state must remain
current within every successful append transaction.

Optional Events.Serialization supplies forward JSON upcasting and PassThrough for
compatible changes. Inventory composes it during historical decoding/full replay; ordinary
inline loading reads saved state without replay or repair. Stored facts are not rewritten.
Projection-meaning changes and reader-before-writer rollout remain consumer decisions.
See the [codec contract](../../Gloaming.Events/Gloaming.Events.Serialization/README.md).
The EF store gains no serialization dependency.

## Explicit consumer operations

The [stock-position command](../../../samples/Wholesale/Worker/Maintenance/stock-regeneration.md)
demonstrates one finite operation with progress, bounded native-conflict retries, cancellation
and process restart. The optional `AggregateRegenerator<TAggregate,TContext>` now owns fresh
attempt scopes/read-committed transactions, replay/save/commit, cleanup and typed outcomes over
the provided rebuilder. An independent ledger adopts it without tenancy/codec dependencies.
Verified changed-header read races and exact maintenance save-concurrency entries permit retry;
persistent ahead state and unrelated failures remain terminal. Acknowledged commit is retained
even if cleanup fails; uncertain commit is not automatically retried. See
[the package contract](../Gloaming.EventSourcing.EntityFrameworkCore/README.md#complete-regeneration-operations).
The regeneration executor supplies no scheduler, durable job queue, multi-target progress or scaling. Admission,
retry values, presentation and online/pause policy belong to the consumer. Full replay remains
in-memory, with cancellation at IO/stage boundaries and uninterrupted synchronous evolution.
Native scope factories own required context establishment before returning each scope; no
per-call initialization hook is needed. Optional IProgress receives ordered Report calls,
while delivery belongs to the reporter. The CLI uses inline reporting; standard Progress<T>
can deliver after completion, with asynchronous handler errors outside the returned outcome.

## Unsupported capabilities

- The provider-free EF store supplies no global feed or secondary projector. Select the optional PostgreSQL module and its complete deployment/transaction contract where needed.
- Snapshot/delta catch-up is unsupported. Current inline state must match the exact head; full replay is separate explicit maintenance.
- Whole-service bootstrap and business-data import remain consumer-owned. The optional PostgreSQL feed supports fixed-source secondary-generation backfill with its committed boundary and safe cursor; it does not deploy a new service, its complete schema or non-event business state.
- Stream deletion/branching, fleet/traffic orchestration, online source-set expansion, automatic history retention and import/export remain unsupported. The optional feed supports its fixed-source publication order, bounded backfill, conditional generation selection and explicit commit-uncertainty outcomes.
- Messaging, audit and cross-module transaction composition remain separately selected consumer integrations.
- Provider-specific pessimistic locking requires a concrete obligation and its own supported contract.

[EF setup, public API, errors and limitations](../Gloaming.EventSourcing.EntityFrameworkCore/README.md) is the consumer contract.
