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
Secondary inline and multi-stream projections are deferred until a concrete query/storage need
justifies them. Main aggregate state must remain current within every successful append transaction.

Optional Events.Serialization now provides explicit forward JSON upcasting and PassThrough
for compatible schema changes. Inventory composes it during historical decoding and full replay;
ordinary inline loading still reads saved state without replay or repair. Event rows are never
rewritten. Shape-only changes need not rebuild correct state; projection-meaning changes and
reader-before-writer rollout remain consumer decisions. See the [codec contract](../../../Gloaming.Events/Gloaming.Events.Serialization/README.md)
and [upcasting evidence](../../../../docs/reports/event-payload-upcasting.md). This EF package gains
no serialization dependency.

## Explicit consumer operations

The [ES3 stock-position command](../../../../samples/Wholesale/Worker/Maintenance/stock-regeneration.md)
demonstrates one finite operation with progress, bounded native-conflict retries, cancellation
and process restart. The optional `AggregateRegenerator<TAggregate,TContext>` now owns fresh
attempt scopes/read-committed transactions, replay/save/commit, cleanup and typed outcomes over
the provided rebuilder. An independent ledger adopts it without tenancy/codec dependencies.
Verified changed-header read races and exact maintenance save-concurrency entries permit retry;
persistent ahead state and unrelated failures remain terminal. Acknowledged commit is retained
even if cleanup fails; uncertain commit is not automatically retried. See
[the package contract](../README.md#complete-regeneration-operations).
Scheduling, durable queues, multi-target progress and scaling remain deferred. Admission,
retry values, presentation and online/pause policy belong to the consumer. Full replay remains
in-memory, with cancellation at IO/stage boundaries and uninterrupted synchronous evolution.
Native scope factories own required context establishment before returning each scope; no
per-call initialization hook is needed. Optional IProgress receives ordered Report calls,
while delivery belongs to the reporter. The CLI uses inline reporting; standard Progress<T>
can deliver after completion, with asynchronous handler errors outside the returned outcome.

## Planned capabilities and their proof obligations

- Async projections and durable checkpoints: prove ordering, duplicate handling, rollback,
  restart and fault recovery. Not just an implementation exercise.
- Multi-stream projections and extra inline views: reassess native EF joins/filter translation
  first. Shared-row updates require real cross-stream concurrency and deadlock proofs; these
  belong before a general projection maintenance engine.
- Snapshot plus delta catch-up: deferred. Current inline state is required at the exact head;
  ordinary fetch does not repair or apply missing events. Full replay repairs data explicitly.
- Projection backfill/rebuild orchestration: new views need an explicit population step, not
  automatic read repair. Prove serving behavior, cutover and concurrency before adding workers.
- Stream lifecycle/deletion, branching, global ordering, pessimistic write modes, subscriptions,
  event metadata/correlation/causation, retention/archiving, import/export and ambiguous-commit
  reconciliation: unsupported. Add bounded concrete obligations before introducing machinery.
- Messaging/outbox, audit integration, cross-module transactions and template event presets:
  excluded from this capability. T1 remains event-free.
- Provider-specific locking (for example SELECT FOR UPDATE): if a real future obligation needs
  it, consider a separate *.Postgres package and discover the abstraction then. The earlier
  advisory-gate package was removed; its proof document is retained as historical evidence.

[EF setup, public API, errors and limitations](../README.md) is the consumer contract.
