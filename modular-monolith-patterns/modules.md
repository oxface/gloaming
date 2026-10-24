# Add modules and aggregate roots

Use [structure](structure.md) for project boundaries. Interpret persistence per aggregate from
the request; ordinary EF state is the default. A module can use more than one persistence style
while retaining one clear native transaction owner.

## Common module steps

1. Create the named implementation and its needed Contracts. Define the requested aggregate
   roots and a small application-facing command/query API. Keep business inputs/results free
   from EF and persistence implementation types.
2. Add a scoped native DbContext with module-owned mappings, schema and migrations.
   Configure a distinct migrations history/schema for modules sharing a database.
   Host connection options are supplied to explicit module registration.
3. Implement current resource permission checks in the application operation. Use the
   application-admitted actor and tenant; neither replaces resource authorization.
4. Make public commands return after their chosen local transaction commits. The module
   application service normally owns the native transaction, save/commit and failure disposal.
   If the application deliberately selects caller-owned staging instead, expose that contract
   explicitly and keep commit ownership visible in its adapter.
5. Map HTTP/worker ingress through the module's Contracts. Native endpoint style and status
   mapping belong to the host. Inter-module calls use Contracts and preserve module ownership.
6. Add migrations and meaningful operation/isolation/rollback tests before considering the
   module ready. Update the application's module map.

An operation performs its accepted business changes and selected audit/outbox/receipt writes
in the same owning transaction. On failure or uncertain commit, dispose the transaction/context
and reconcile from fresh state. Independent module commits have independent recovery obligations.

## State-stored aggregates

Use ordinary domain classes or immutable state/candidates with native EF mappings.
Keep invariants, complete-candidate validation and optimistic versions in the module.
A generic repository, universal entity base and CQRS dispatcher are not required.

Choose native concurrency tokens for contested writes. Check expected versions at the business
boundary and handle save-time concurrency as well. Validate the complete accepted change before
mutating persisted state. Child/value mappings remain application decisions.

For multitenant data, select [ownership/save protection](gloaming.md) and include tenant identity
in all relevant keys/predicates. A query filter alone does not protect detached updates,
relationships or raw SQL. Use native constraints and selected RLS where the threat model requires.

Done: create/load and the selected mutation preserve invariants, ownership and versions;
stale/invalid changes and rollback leave committed state unchanged.

## Event-sourced aggregates

Select Gloaming's EventSourcing core and native EF store, then read their exact current contracts.
Use consumer-owned immutable state/facts and pure evolution. Stable event names/schema versions
and JSON encoding belong to the module; persisted CLR names are not the event contract.

Configure the complete stream key, event/header mappings and one required inline aggregate
state. Registered aggregate writes stage the accepted events, head and inline state together.
Install every required sync/async save validation. Save/commit stays with the selected operation
owner; the store is not an automatic unit of work.

Ordinary aggregate loading uses validated inline state at the current head.
Explicit history reading and complete regeneration are separate operations; production reads
do not silently rebuild or emit historical side effects. Payload upcasting is independently
selected. Reducer changes that affect persisted state require a deliberate repair/rollout plan.

For the initial example, use the named aggregate's smallest create/load lifecycle with an
explicit creation fact. Add business decisions only from actual requirements.

Done: native tests prove contiguous facts, consistent event/head/inline-state versions,
optimistic conflicts, complete rollback and replay of retained literal data when compatibility
is promised. State-stored aggregates in other modules take no event-sourcing dependency.

## Cross-module behavior

Same-process invocation is enough only when its failure/commit behavior meets the requirement.
If one module commits and the caller can die before recording the result, select a durable
handoff or explicitly prove a shared native transaction. Follow [messaging](messaging.md) when
durable progress is required; compensations remain business decisions.
