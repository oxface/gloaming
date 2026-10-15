# D2.3: module consistency and coordination research

Date: 2026-10-15 (Europe/Kiev). Owner-selected research, now prepared for review in the
[comparison and recommendations](../reports/d2-3-module-consistency-and-coordination.md).
The report separates fresh primary-source/code inspection from historical runtime evidence.
It introduces no cross-module transaction abstraction or change to the local-dispatch contract.

The requirements decide the transaction boundary. Retain module-owned durable orchestration for
independently committed steps. Assess broker-free delivery by composing existing inbox/outbox
through public module intake Contracts before extracting another mechanism. A shared native
transaction is a conditional alternative when the business requires atomic acceptance across
modules in one physical database; provider support is not a completed Gloaming consumer proof.
The owner subsequently selected [D2.3a local durable delivery](d2-3a-local-durable-module-delivery.md)
with “proceed”; its [consumer proof report](../reports/d2-3a-local-durable-module-delivery.md)
records the executable adoption separately from this research. Shared-transaction adoption remains
a separate candidate.

## Question and existing evidence

When one module accepts a business change and another module must react, compare:

1. Durable messages with independently committed module transactions and choreography.
2. A durable process owner coordinating module commands/replies, including refusal and compensation.
3. Explicit in-process calls through module Contracts under one shared native database transaction.

These are not three mutually exclusive technologies: a saga can use durable inbox/outbox, and
in-process dispatch can also relay durable work. Separate control flow, delivery and transaction
ownership before choosing an implementation.

[WF1](../reports/wf1-durable-inter-module-workflow.md) already proves durable Sales/Inventory
request/reply progress with separate module databases. [WF2](wf2-durable-continuations.md) adds
consumer-owned branching/compensation and retained continuation execution. Those are existing
evidence for independently committed workflows, not a comparison against shared native transactions.
[D2.2](d2-domain-events-and-required-reactions.md) remains planned for durable deferred reactions
within an explicitly chosen ownership boundary. The initial D2.1/EF1 work concerned local save
dispatch, not the architectural merits of all three module consistency choices.

The [fresh independent domain-event reassessment](../reports/domain-events-independent-reassessment.md)
addresses terminology, local use cases, timing and current Wolverine behavior. D2.3 should use those
findings without treating existing Gloaming interfaces as the target architecture.

Owner clarification, 2026-10-15: a business module owns its saga and compensation decisions. Do not
move these into the host by default. In-process calls through other modules' public Contracts are
allowed as a design candidate; establish durable intent, target idempotency and recovery when calls
commit independently. Gloaming already retains workflow state and an atomic next outgoing request,
but supplies no proven automatic direct-contract delivery/reply adapter. The state durability review
is recorded in the [local policy and ordering report](../reports/d2-1e-local-order-and-workflow-durability.md).

## Required comparison

Use a concrete process such as placing an order and reserving stock. Identify what Order owns,
what Inventory owns and whether Product/catalog data actually needs mutation. Decide whether a
refusal means the order was never accepted, remains Pending, becomes Rejected or needs compensation.
"The other change must happen" must distinguish transient failure from a legitimate business refusal.

For each choice, document:

- The business invariant and acceptable visibility of intermediate state; completion and response meaning.
- One physical database with separate schemas versus separate databases. Private table ownership
  and shared transaction ownership are different isolation choices.
- Calls through public module Contracts; the coordinator must not access private contexts/tables.
  Decide whether the process belongs to a business module or a separate application process component.
  A generic host layer must not acquire business policy merely because it assembles the application.
- Atomicity, operation scope, connection/transaction enlistment, commit authority and lock duration.
  Native EF sharing requires the same DbConnection and DbTransaction; identical connection strings
  or a wrapper around independent SaveChanges calls do not establish one local transaction.
- Durable intent, duplicate and semantic identity, idempotency, ordering and concurrent updates.
- Crash before/after producer commit, after receiving work, before receiver commit, and after an
  effect but before acknowledgment. Explain the retained evidence used for recovery at each cut.
- Retry exhaustion, poison work, receiver business refusal, compensating refusal, operator recovery
  and unknown commit outcome. Avoid promising eventual success from retries alone.
- Local durable delivery without a broker versus remote transport. Process placement must not be
  confused with storage/durability, and adding a broker must not be assumed necessary for durability.
- Coupling through business requirements, module Contracts, runtime availability and transaction
  infrastructure. Message-based collaboration still has semantic coupling.

## Proof obligations before adopting a mechanism

If the comparison selects shared native transactions, prove both module saves remain uncommitted
until one owner commits; a failure in either module rolls back both, with no private-table access.
Check provider support, mismatched transactions, independent-commit attempts, cancellation,
concurrency, deferred commit failure and fresh-state retry. Do not infer PostgreSQL compatibility
solely from EF documentation's SQL Server example.

If durable choreography or orchestration is selected, reuse existing proven inbox/outbox and workflow
capabilities where their contracts fit. Prove atomic retained work, process death/restart, duplicate
and reordered delivery, concurrent reactions, permanent refusal and visible intermediate states.
Do not add a second scheduler, saga engine, event store or universal integration-event mapper.

If local synchronous events remain useful inside a module, independently prove their ordering
requirements with multiple aggregate sources and handler-produced events. Subscription routing is
not a dependency graph or a workflow scheduler. An operation requiring B before C must make that
dependency explicit rather than relying on tracker enumeration or incidental registration order.

## Deliverable and extraction gate

Produce a source-backed tradeoff report and one concrete recommendation per stated requirement.
Any executable comparison or implementation is a separately selected increment. A defensible result
can be consumer-owned orchestration plus documentation, with no new reusable mechanism. The
checkpointed EventReactions capability supplies local buffering/dispatch without imposing DDD
inheritance or marker interfaces; it supplies no cross-module delivery guarantee.

No current runtime or template behavior changes with this plan. No new reusable mechanism is proven.
This research adds no runtime tests or execution evidence; existing WF1/WF2 results remain historical.

## Primary starting points

- [Microsoft domain-event implementation guide](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation):
  local aggregate reactions and pre-/post-commit choices.
- [EF cross-context transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions#cross-context-transaction):
  native connection/transaction requirements.
- [Transactional outbox](https://microservices.io/patterns/data/transactional-outbox.html) and
  [saga coordination](https://microservices.io/patterns/data/saga.html), by Chris Richardson.
- [Process Manager](https://www.enterpriseintegrationpatterns.com/patterns/messaging/ProcessManager.html),
  by the Enterprise Integration Patterns authors.
