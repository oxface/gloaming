# Wholesale consumer modules

The API and reservation workers compose Access, Sales and Inventory through separate Contracts
and module-owned persistence. These are consumer-owned modules; Gloaming does not prescribe
their project layout, business policy or Contracts.

| Module | Ownership | Persistence |
| --- | --- | --- |
| [Access](Access/README.md) | Application users, exact external identities, canonical Organizations, current membership admission and authorized membership suspension/reactivation. | Global `access` schema, separate migration history; admission runs before tenancy. Administration owns its native transaction and accepted-change audit. |
| [Sales](Sales/README.md) | Customer profiles, permitted draft orders/lines, submission allowances, stock-issue requests and compensating reservations. | Tenant-owned `sales` schema, separate migrations, inbox/outbox, audit and process state. |
| Inventory | Catalog availability, event-sourced stock positions, receipts, issues and reservations. | Tenant-owned `inventory` schema, separate migrations, inline state, inbox/outbox and audit. The availability catalog is separate state-stored data. |

Each implementation references its own Contracts and communicates with peers through Contracts.
It never references a peer implementation or host. Contracts expose plain business values and
results, without EF, HTTP or Gloaming execution contexts. Sales.Contracts explicitly references
Inventory.Contracts for Inventory-owned outcomes. These boundaries are checked by
[architecture tests](../../../tests/ArchitectureTests/SampleModuleBoundaryTests.cs).

The [API composition](../Api/DemoComposition.cs) selects native contexts/providers, module
services and HTTP resolvers explicitly. [Endpoints](../Api/Endpoints/OrganizationEndpoints.cs)
call Contracts; identity mapping and Organization admission live in the host. Public native
contexts and finite seed helpers serve composition, migrations and setup. Seeds only stage
rows; the caller establishes tenancy and saves/commits. Runtime never migrates or seeds.

## DDD sample patterns

Sales owns immutable profile details and complete replacement candidates. Invalid edits,
stale versions and persistence failure leave accepted data intact; profile rows and selected
audit commit together in the owning transaction. Inventory uses the optional
[aggregate base](../../../src/Gloaming.EventSourcing/Gloaming.EventSourcing/README.md) for
immutable accepted state and pending facts. Quantity/stock rules remain Inventory policy.

## Required local submission reactions

Sales submission requires an allowance reservation in the same native transaction.
[The Sales guide](Sales/README.md) records its local handlers, save/commit obligations and
bounded policy. This is not durable intermodule delivery or automatic domain-event publication.
[Reaction proofs](../IntegrationTests/Reactions/ReactionTests.cs) retain fault/rollback coverage.

## Rich state-stored Sales draft

Sales owns relational draft children, complete-batch edits, root version concurrency and positive
resource permissions. The [API](../Api/README.md) selects authenticated/admitted routes, antiforgery
and late resource checks. Its optional restricted RLS composition retains narrower existing-draft
and profile authority; it does not acquire create/submit authority from tenant context.

## Durable stock-issue progress

[Broker workers](../Worker/Broker/README.md) and [local workers](../Worker/Local/README.md) run the
same Sales/Inventory reservation policy through public Contracts and independent native commits.
Local intake establishes a fresh receiving scope and acknowledges committed receipt. Broker
intake composes the existing inbox/outbox protocol. Compensation attempts, deadlines and operator
retry are consumer policy; no distributed transaction or general workflow engine is supplied.

## Event-history reads

[Inventory persistence proofs](../IntegrationTests/Scenarios/EventPersistence/README.md) retain
literal histories, tenant-isolated current/version/as-of reads, captured-head consistency,
invalid history and forward migration of the existing catalog. History reconstruction remains
separate from command loading through required inline state.

## Explicit event commands

Inventory opening, receipts and issues use the provided event store. The caller owns its native
transaction and save/commit; `Changed` is a proposal until commit. Complete-batch validation,
expected version and tenant ownership precede staging. Provider/concurrency failure requires
rollback, disposal and a fresh decision; an ambiguous commit is not proof of rollback.
Use the real [Inventory operator commands](../Worker/README.md#module-operations) for consumer usage.

## Inline decision state and committed queries

One required stock-position state must agree with its captured stream head. Missing/behind/
corrupt state is rejected, not silently repaired during command loading. The optional
[explicit regeneration command](../Worker/Maintenance/stock-regeneration.md) owns existing-target
repair, progress, cancellation and restart. [Gloaming's contract](../../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md)
records append/rebuild validation and limitations; migrations do not populate existing states.

## Inventory durable incoming stock commands

Inventory explicitly admits its configured producer and Organization, establishes fresh context
and invokes the existing business command. Accepted facts/state, audit, reply and inbox completion
share the processor's selected native transaction. Business refusal completes with a decline;
technical/admission failure follows the caller-selected retry policy. Deduplication grants no
authority. See [intake proofs](../IntegrationTests/Scenarios/EventPersistence/README.md#durable-stock-issue-command-intake)
and the [messaging contract](../../../src/Gloaming.Messaging/docs/inbox.md).
