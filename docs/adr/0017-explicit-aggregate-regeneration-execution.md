# ADR 0017: Explicit aggregate regeneration execution

Status: accepted and implemented, 2026-10-17. Extends the optional maintenance capability
in ADR 0008 with explicit native scope-factory and IProgress integration. Native optimistic
concurrency and direct rebuilder/store save ownership remain unchanged.

Provide `AggregateRegenerator<TAggregate,TContext>` in the existing EventSourcing EF package.
An explicit call delegates one complete maintenance operation: fresh scope/context per attempt,
read-committed native transaction, provided full replay, save/commit, disposal, bounded recognized
conflict retries and typed progress/results. Reuse the existing private maintenance association
to verify context/transaction pairing and exact save-concurrency entries. No new provider,
scope-factory protocol, hosted service, scheduler, queue, lease or transaction-adoption mechanism.

Keep the provided rebuilder independently usable as a staging operation whose caller owns
save/commit. Ordinary reads/writes retain strict inline-state behavior. Optional execution has
an explicit typed context. Its native scope factory must return scopes with any already-authorized
immutable consumer context established before native dependencies resolve. Inventory owns that
factory decoration and failure cleanup. There is no per-call initialization callback or required
actor/tenant package or module Contract.

Ahead state requires distinguishing persisted corruption from a writer advancing between reads.
Re-read the filtered header and report a retryable stale capture only for a valid advanced
version/new stamp with unchanged complete key/creation metadata and a head covering observed
state. Otherwise retain the integrity failure. Arbitrary constraints and unrelated concurrency
are not broadly retried. Provider-specific insert conflicts can remain terminal failures.

Report terminal outcomes after cleanup. Acknowledged completion remains known even when cleanup
fails, with that diagnostic retained. Once commit begins, an acknowledgment failure/cancellation
is indeterminate and is not automatically retried. Optional native IProgress receives ordered
pre-commit/attempt Report calls; delivery belongs to the reporter and may outlive the operation.
Inventory's CLI supplies synchronous reporting; asynchronous handler errors are not operation
outcomes. Consumers format returned terminal results. Restart explicitly replays from
retained history, without external side effects or a resumable checkpoint.

The consumer owns target/admission, retry values, progress presentation, scheduling,
consistent reducer rollout and pause/drain policy. Full-prefix materialization and uninterrupted
synchronous evolution remain bounded-scope limitations; no large-stream or mixed-version rollout
guarantee follows. See the [execution contract](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md).
