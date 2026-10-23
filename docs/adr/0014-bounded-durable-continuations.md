# ADR 0014: bounded explicit durable continuations

Status: public interface and exact replacement scope accepted by the owner on 2026-10-12.
Implementation awaits line-by-line code review. Supersedes [ADR 0013](0013-fixed-sequential-saga-progress.md)
before the sequential experiment was checkpointed.

The owner approved the final reply/signal/input-pause names on 2026-10-13. Persisted numeric
status values and continuation identities are retained.

## Decision

Provide optional Messaging.Workflows definitions and a native EF adapter executing only a
saved, typed continuation method. Do not replay an entry point, interpret a graph or generate a
handler harness. Consumers use ordinary C# for branching and compensation, stable wire names
for reply bindings, and explicit typed signal bindings for external input. Operator recovery
is one consumer use of an input pause; the technical status does not imply business failure.

A provided module-owned process table stores initial input, consumer state and requested
operations/semantic outcomes. Its `(WorkflowType, Id)` primary key is globally unique within a
type across tenants; optional TenantKey is checked on lookups and original-value concurrency.
Scalar Version advances once per accepted turn and arbitrates native competing writers.

The runner allocates request IDs, matches replies, restores detached candidate state, executes
the selected method and tracks process/returned outgoing request together. Mandatory native
save validation binds those tracked proposals to their originating transaction. Direct callers
own save/commit; existing inbox processing owns reply save/completion/commit. Existing outbox
workers deliver requests. No workflow worker or transport dependency is introduced.

## Consequences

The library removes repeated operation bookkeeping and request enqueue mechanics while leaving
business decisions, Contracts, full outcome validation, producer/tenant/actor admission and
transport wiring explicit. One pending request is supported; intervention requires a consumer
operator signal. Technical execution status is separate from business state.

Definitions use a single persisted version; incompatible changes need explicit migration. JSON
operations retain history indefinitely and have no throughput/pruning claim. PostgreSQL is the
first proven database. Parallel joins, scheduling, deadlines, cancellation, durable operator
signal receipts, replay/graph engines and generic compensation selection remain deferred.

[Approved scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/wf2-durable-continuations.md),
[current package-local contract](../../src/Gloaming.Messaging/docs/workflows.md), and
[fresh evidence](../reports/wf2-durable-continuations.md).
