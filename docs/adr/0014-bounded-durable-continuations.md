# ADR 0014: bounded explicit durable continuations

Status: accepted and implemented, 2026-10-12.

Persisted numeric status values and continuation identities are compatibility contracts.

## Decision

Provide optional definitions and execution together in Gloaming.Messaging.Workflows.EntityFrameworkCore,
retaining the two public namespaces. Definitions take its EF dependency; ordinary Messaging
remains independent. Execute only a saved, typed continuation method. Do not replay an entry point,
interpret a graph or generate a handler harness. Consumers use ordinary C# for branching and compensation, stable wire names
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

Save validation verifies any unchanged process/request proposal against native stored values
in that same transaction. Matching tracked values alone cannot distinguish completed writes
from premature acceptance or savepoint rollback. JSON comparison remains semantic; generated
request delivery fields are excluded. Repeated saves require native SELECT access to all mapped
process/request columns. Native key lookup ignores EF filters while database permissions/RLS
remain effective. Explicit validation stays synchronous; registered async validation awaits
the native reads with cancellation. No new public lifecycle hook, schema or execution protocol
is introduced.

## Consequences

The library removes repeated operation bookkeeping and request enqueue mechanics while leaving
business decisions, Contracts, full outcome validation, producer/tenant/actor admission and
transport wiring explicit. One pending request is supported; intervention requires a consumer
operator signal. Technical execution status is separate from business state.

Definitions use a single persisted version; incompatible changes need explicit migration. JSON
operations retain history indefinitely and have no throughput/pruning claim. PostgreSQL is the
first proven database. Parallel joins, scheduling, deadlines, cancellation, durable operator
signal receipts, replay/graph engines and generic compensation selection remain deferred.

See the [package-local contract](../../src/Gloaming.Messaging/docs/workflows.md) and [deployment guidance](../../src/Gloaming.Messaging/docs/workflow-deployments.md).
