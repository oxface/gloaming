# D2.3: module consistency and coordination

Date: 2026-10-15 (Europe/Kiev). Scope: the comparison selected in the
[D2.3 plan](../plans/d2-3-module-consistency-and-coordination-research.md).
This is research and a review of existing evidence. It adds no runtime mechanism,
template behavior or executable comparison. Source research proves no new reusable
mechanism. Recommendations below are design judgments, distinguished from observations
of external implementations and local evidence.

Subsequent owner-selected [D2.3a adoption](d2-3a-local-durable-module-delivery.md) implements and tests
the broker-free consumer handoff proposed here. This research retains its original evidence boundary;
fresh execution results belong to that separate report.

## Recommendation

Keep module-owned durable workflows for processes whose participants commit independently.
Keep business decisions and compensation in the owning module. Prefer existing inbox/outbox
and workflow primitives for durable delivery through public module Contracts; a broker is
an optional transport choice. Persisting the saga alone does not make arbitrary method calls
recoverable.

Use explicit orchestration under one native database transaction when the requirement is
that both module changes become visible together and both participants accept that transaction
boundary. Keep the transaction owner outside participant persistence implementations and
invoke their public Contracts. This preserves private table ownership while deliberately
sharing commit ownership. It requires a separate executable PostgreSQL proof before adoption.

Use choreography for independent reactions whose producer does not own completion of the
whole process. For an order placement with reservation, rejection, cancellation and later
compensation, the recommended owner is Sales and an explicit durable process is easier to
inspect than a distributed subscription chain. This does not require moving policy into
the host or introducing another saga engine.

## Concrete process and response meaning

The following is proposed sample policy, not a universal order model:

- Sales owns order identity, price/line snapshots, the placement process and its customer-facing status.
- Inventory owns available stock, reservation identity, expiry and release rules.
- Catalog owns product descriptions and saleability. Placing an order need not mutate a Product
  aggregate. Reading catalog data and reserving stock are different requirements.
- `PendingStock` means Sales durably accepted a placement request, not that it accepted a
  fulfillable order. `Confirmed` requires a successful reservation result accepted by Sales.
  An ordinary out-of-stock refusal moves the order to `Rejected`; an infrastructure failure
  leaves it pending or moves it to an explicit intervention state.
- Cancellation after stock was reserved requires release, or a business-defined escalation
  when release is refused. A timeout does not prove that Inventory made no reservation.

The completion contract must say which of these happened. A synchronous successful response
after one shared commit can mean order and reservation both exist. An asynchronous intake
acknowledgment means durable work exists; it must expose pending status and a way to obtain
the final result. Waiting in an HTTP request for a final reply changes latency, not the
participants' independent commit boundaries.

Do not split one Inventory invariant into a saga merely because an order has multiple lines.
If all reservations must succeed together and Inventory owns them, a public multi-line reservation
command can decide them in one Inventory transaction. A saga is justified by independently committed
business steps across owners. The existing two-line WF2 sample proves the continuation and compensation
protocol; it does not establish the right transaction boundary for every order system. This is a
modeling recommendation, not a new capability or a change to the sample.

This distinction matches the saga pattern's sequence of local transactions and the Process
Manager's explicit responsibility for deciding subsequent work. Their descriptions establish
coordination patterns, not a prescribed host-owned layer.
[Saga pattern](https://microservices.io/patterns/data/saga.html),
[Process Manager](https://www.enterpriseintegrationpatterns.com/patterns/messaging/ProcessManager.html).

## Compare ownership and guarantees

| Requirement | Durable choreography | Module-owned durable orchestration | One shared native transaction |
| --- | --- | --- | --- |
| Who decides what happens next? | Each subscribing module owns its reaction. | Sales owns placement progress; participants own their decisions. | An explicit application operation calls both Contracts in order. |
| Atomicity | Each participant transaction; intermediate state is visible. | Each participant transaction; workflow progress is retained separately. | All enlisted database effects commit or roll back together. |
| Order completion | Must be modeled explicitly across replies; an event alone does not prove completion. | Explicit pending, confirmed, rejected and compensating states. | Successful owner commit establishes both changes. |
| Persistence placement | Works with separate schemas or databases. | Works with separate schemas or databases. | One physical database and native connection/transaction for this candidate. |
| Runtime availability | Producer can commit while consumer is unavailable if delivery is retained. | Process can wait while participants are unavailable. | Both participants and the database must be usable during the operation. |
| Coupling accepted | Event semantics and reaction policy. | Command/reply semantics, process states and compensation. | Contract semantics plus connection, isolation, lock and commit conventions. |
| Best fit here | Independent follow-on work with no central completion decision. | Multi-step order placement with refusal, deadlines and compensation. | Short operation with a strict all-or-nothing visibility requirement. |

Private schemas are an ownership convention, not automatically separate commit domains.
Conversely, one process does not make two database commits atomic. Messaging removes the
need for simultaneous availability only when work is retained and recoverable; it still
couples modules through business meanings.

The transactional outbox pattern retains outgoing messages with the producer's state and
uses a relay. Duplicate publication remains possible, so receivers require idempotency.
That is the delivery basis for either choreography or orchestration, rather than a reason
to choose one control-flow style over the other.
[Transactional outbox](https://microservices.io/patterns/data/transactional-outbox.html).

## Durable public Contracts within the monolith

There are two materially different meanings of an in-process module call:

1. **Durable intake:** the public Contract accepts a retained command into the receiver's
   inbox and returns after committing that intake. A worker later performs the business
   operation under the receiver's transaction. Its outcome is a durable reply.
2. **Immediate business execution:** the public Contract performs and commits the target
   operation during the call. Recovery must distinguish a lost response from an operation
   that never committed.

Prefer the first as the next Gloaming consumer proof, reusing existing delivery primitives
where their contracts fit. Proposed sequence:

1. Sales commits `PendingStock`, workflow state and `ReserveStock` outbox intent together.
2. A relay invokes Inventory's public durable intake. It acknowledges the Sales outgoing
   delivery only after Inventory commits the inbox row. A lost acknowledgment causes retry.
3. Inventory processes its inbox under its transaction, commits the reservation or retained
   refusal, records completion and enqueues its reply atomically.
4. The reply relay durably delivers to Sales' inbox. Sales then applies the correlated result
   and advances its workflow under its transaction, retaining any next outgoing command.

The relay calls Contracts, not private tables or contexts. A retained inbox row is evidence
of intake, not a successful stock reservation. Existing workers and retention policies remain
responsible for delivery/recovery; this proposal adds no scheduler. This composition is a
candidate for a sample, not a new proven adapter or a guarantee supplied by this research.
The proposed public intake Contract and module-owned adapter do not already follow from
having message records in a Contracts assembly. The adapter must create its own fresh
operation scope, persist intake under its native transaction and return durable receipt
only after commit. Calling an enqueue/receive method that merely stages a row is insufficient.
Do not expose a private `DbContext`, or a context-typed inbox, to the Sales coordinator.
Preserve module admission of producer, tenant, message schema and correlation, with fresh operation
context for processing. Running in one host does not authorize payloads or permit reusing the
sender's initialized tenant/actor scope for the receiving module's operation.

For the second approach, retain the command before calling the target. Inventory must
atomically store the reservation/refusal and an original result associated with a stable
operation identity. It must either retain reply intent in that transaction or offer a
reliable way to replay the retained result into durable delivery. Only then can a caller
recover after a target commit followed by a lost return value. Retrying a plain method
because the saga still says “waiting” is insufficient. This is an inference from the outbox
failure boundary, not a claim that any saga library automatically wraps ordinary calls.

MassTransit's transactional outbox distinguishes retained outgoing messages from an
in-memory buffer; its consumer outbox combines received-message tracking and outgoing
storage. Those are useful obligations to reuse, without adopting its configuration surface.
[MassTransit outbox](https://masstransit.massient.com/concepts/outbox).

## Identity, order and concurrency obligations

For the proposed order process, carry an operation identity such as tenant, order,
placement attempt and step. A transport delivery identifier and workflow correlation are
separate concerns: an operator can resend the same business request with a new message ID.
Inventory should return the original result for the same semantic request, reject the same
identity with conflicting input, and retain that evidence for the business retry/replay
horizon. Release has its own identity and refers to the reservation it reverses.

Enforce idempotency with the business effect in one target transaction. A marker committed
before an effect can suppress unfinished work; one committed after an effect permits
duplicates. Retain refusal outcomes too: retry must not silently turn the same operation
into a different decision after stock changes. If a new attempt is desired, model it as new
business intent.

Do not depend on a global event order. Correlate responses to the expected process step and
version; reject or reconcile stale outcomes. Concurrent placements need Inventory's stock
invariant enforced through its own locking, versioning or conditional update rules. Concurrent
replies/cancellation need Sales' process transition checks. A late reserve success after Sales
timed out may require release; it must not resurrect a cancelled order implicitly.

Wolverine's documentation explicitly distinguishes envelope deduplication from logical
business identity, and documents bounded retention. That supports the identity distinction;
it does not establish perpetual exactly-once effects or protect arbitrary external calls.
[Wolverine idempotency](https://wolverinefx.net/guide/durability/idempotency.html).

## Crash cuts and retained recovery evidence

This table describes the recommended durable-intake protocol. The same obligations apply
to a choreographed participant that changes state and emits a follow-on event.

| Failure point | Evidence and required recovery |
| --- | --- |
| Before Sales commit | No accepted placement or outgoing intent; retry the incoming request with its stable identity. |
| After Sales commit, before relay | Sales outbox retains the reserve request; a restarted worker can deliver it. |
| After Inventory intake, before producer acknowledgment | Inventory inbox retains the request; retry intake under the same identity and acknowledge only durable receipt. |
| During handling, before Inventory business commit | Target effect, completion and reply intent roll back together; inbox work remains retryable. |
| After Inventory effect, before reply delivery or worker acknowledgment | Target result/completion and reply outbox survive; do not repeat the reservation. Relay retained reply. |
| After Sales reply intake, before advancing the workflow | Sales inbox retains the reply; continuation can be retried against expected workflow state. |
| After Sales transition, before acknowledgment | Transition/completion/next command were atomic; duplicate input must not advance again. |
| During an uncertain commit | Reconnect and inspect durable operation/result evidence; an exception alone does not prove rollback. |

Transient failures may be retried with bounded policies. Poison payloads and permanent
technical failures need retained diagnostic state, alerting and controlled retry or repair.
Out-of-stock is a business result, not an infrastructure retry. Exhausting retries cannot
make stock exist or guarantee completion.

Compensation is another business operation, not a reversal of database time. Releasing a
reservation may fail, expire or be refused after shipment. Sales must retain that progress
and expose an intervention/refusal state when automatic recovery cannot finish. Microsoft's
compensation guidance likewise treats compensation as application-specific, retryable work
that may need manual recovery.
[Compensating transaction](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction).

## Shared EF/PostgreSQL transaction candidate

Observed native requirements: EF cross-context transaction sharing uses both the same
`DbConnection` and `DbTransaction`, with explicit `UseTransaction` enlistment. Its normal
savepoint support can roll a failed save back within an existing transaction; that does not
itself roll back earlier participant work or in-memory state. These APIs are relational
features, not proof that any two configured contexts are coordinated.
[EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

Npgsql documents one active transaction per connection, savepoints, and cautions against
depending on its distributed transaction support because recovery is incomplete. Thus the
candidate here is one open PostgreSQL connection and transaction across schemas, not two
connections, separate databases or implicit `TransactionScope` escalation.
[Npgsql transaction behavior](https://www.npgsql.org/doc/basic-usage.html#transactions).

Proposed consumer operation: Sales owns the placement decision. An application composition
boundary establishes the native transaction; Inventory's implementation enlists through its
own setup and is invoked through its public Contract. Inventory saves a reservation, Sales
saves the confirmed order, and only the operation owner commits. A participant may flush but
must not independently commit, replace or dispose the shared connection/transaction. The
coordinator never accesses either participant's private context/table. How to supply that
enlistment without leaking native persistence into business Contracts belongs in a concrete
consumer proof, not a speculative shared interface.

The process must stay short: no waiting for remote work, user input or deferred delivery
inside the transaction. PostgreSQL's lock documentation shows why longer transactions and
inconsistent lock order create contention/deadlocks. Sharing a transaction does not by
itself choose correct isolation or prevent overselling. PostgreSQL's default Read Committed
allows statement snapshots to differ; stronger isolation can require replaying the whole
transaction after serialization failure.
[PostgreSQL 18 locking](https://www.postgresql.org/docs/18/explicit-locking.html),
[PostgreSQL 18 isolation](https://www.postgresql.org/docs/18/transaction-iso.html).

Native savepoints roll back database work after their boundary, not the module's consumed
in-memory facts or previously accepted entity state. For this candidate, abort the whole
operation on participant failure and rebuild both module scopes for retry. PostgreSQL also
documents that rollback to savepoint preserves the surrounding transaction; a successful
rollback to one is not a completed all-or-nothing operation.
[PostgreSQL savepoints](https://www.postgresql.org/docs/18/sql-rollback-to.html).

Retries must replay the complete operation with fresh connection, transaction and module
state, rather than retrying only the second save. EF documents grouping user transactions
inside the execution strategy and warns of uncertain commit outcomes. Verify success using
a durable operation identity before replaying ambiguous results. Npgsql exposes a provider
retrying strategy, but that is not automatic coordination of participant lifecycles.
[EF retry and commit uncertainty](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency),
[Npgsql execution strategy](https://www.npgsql.org/efcore/misc/other.html#execution-strategy).

Cancellation needs its own proof. Npgsql exposes cancellation on async commit and rolls
back a transaction that remains pending when disposed; neither means a cancelled caller
can assume an unobserved commit failed. Before commit, abort and dispose the operation.
If cancellation/connection loss obscures commit outcome, report uncertainty and verify
durable identity through fresh state before retrying. PostgreSQL's protocol documentation
also warns that sending a cancellation request alone does not establish its effect.
[Npgsql transaction API](https://www.npgsql.org/doc/api/Npgsql.NpgsqlTransaction.html),
[PostgreSQL cancellation](https://www.postgresql.org/docs/18/libpq-cancel.html).

The adoption proof must observe both saved changes as uncommitted from an independent
connection, then show one owner commit publishes both and any participant failure publishes
neither. Include mismatched enlistment, participant commit attempts, cancellation before and
during commit, concurrency, a deferred constraint failing at commit, and fresh-state retry.
No such cross-module proof was added or claimed in this slice.

## What to borrow from MassTransit and Wolverine

The useful precedent is separating retained intent, delivery, participant transaction and
process state. MassTransit's saga examples address concurrent transitions with persistence,
retry and outbox behavior rather than treating a persisted state machine as sufficient.
Wolverine's saga walkthrough persists the state and cascading work through its persistence
integration. These are evidence for distinct lifecycle obligations, not a reason to copy
either library's saga base class, discovery conventions or full transport configuration.
[MassTransit saga state machines](https://masstransit.massient.com/concepts/saga-state-machines),
[Wolverine saga persistence walkthrough](https://wolverinefx.net/guide/durability/sagas.html#how-it-works).

Durability does not require RabbitMQ or a separate process. Wolverine's durable local queues
persist local messages for restart recovery, while durability must be configured alongside
message storage. MassTransit's PostgreSQL SQL transport uses database-backed queues and
offers explicit receive-order modes. Its in-memory transport loses messages on process
termination. The lesson for Gloaming is to prove a retained local handoff, not to add a new
transport product or assume an in-memory mediator is durable.
[Wolverine local queueing](https://wolverinefx.net/guide/messaging/transports/local.html#durable-local-messages),
[Wolverine durable messaging](https://wolverinefx.net/guide/durability/),
[MassTransit SQL transport](https://masstransit.massient.com/configuration/transports/sql),
[MassTransit in-memory transport](https://masstransit.massient.com/configuration/transports/in-memory).

These are first-party documentation observations retrieved 2026-10-15. PostgreSQL links are
pinned to version 18. EF's transactions page reports update 2026-08-31; its retry article
reports 2022-07-26. Current MassTransit URLs redirect to documentation identifying v9;
Wolverine documentation includes 6.x version-specific additions. These moving documentation
pages establish precedents, not a tested version compatibility matrix or a proposed dependency
upgrade. No MassTransit/Wolverine package was installed or tested for this slice.

## Local implementation evidence

Fresh source inspection was performed after merge checkpoint `5f90abe` on
`feat/ddd-sample-patterns`. The repository targets .NET 10, EF Core 10.0.12 and the Npgsql EF provider
10.0.3 in [central dependencies](../../Directory.Packages.props). These are the local baseline;
external documentation is not a substitute for executing the provider-specific comparison.

| Inspected mechanism | Finding and boundary |
| --- | --- |
| [EfWorkflow](../../src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/EfWorkflow.cs) | An accepted turn tracks the process state, scalar version, pending command/continuation and operation ledger, then queues the returned request through the owning typed outbox. It requires a native transaction and rejects ambient/enlisted work. It does not save, commit or recover arbitrary calls made inside a continuation. |
| [PostgresInbox](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInbox.cs) | ReceiveAsync inserts or compares retained intake in an explicit ReadCommitted transaction. It never commits; Queued and AlreadyReceived are provisional. The future module intake must await its own commit before returning durable acceptance. |
| [PostgresInboxProcessor](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxProcessor.cs) | A fresh context starts the transaction, locks one incoming row, invokes the handler, saves local work, records completion and commits. Retried work requires fresh scopes. An independently committed external effect is outside that transaction. |
| [Publisher contract](../../src/Gloaming.Messaging/Gloaming.Messaging/IMessagePublisher.cs) and [outbox dispatcher](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs) | Publication acceptance is distinct from downstream business completion. A committed lease precedes publication; completion is conditional on the lease. Ambiguous acceptance can redeliver the same message, so a receiver must retain compatible duplicate evidence. The publisher seam suggests local durable intake without changing the dispatcher, but that composition has not been executed here. |
| Inventory [reservation handler](../../samples/Wholesale/modules/Inventory/Inventory/Reservations/StockReservationHandler.cs), [receipt](../../samples/Wholesale/modules/Inventory/Inventory/Reservations/StockReservation.cs) and [mapping](../../samples/Wholesale/modules/Inventory/Inventory/Reservations/StockReservationMapping.cs) | Tenant/reservation identity retains original inputs and the decision, including refusal. Duplicates with different delivery IDs return that decision; changed inputs fail. Release uses current stock and records release state. The handler queues its reply in the same transaction used by inbox processing. Neither delivery deduplication alone nor a saga row provides this business idempotency. |
| Sales [workflow definition](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/ReservationWorkflow.cs) and [application service](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationOrders.cs) | Sales owns sequencing and compensation. Start and operator retry currently begin, save and commit their own transaction. They cannot simply be wrapped as shared-transaction participants; any future participant must have an explicit noncommitting execution contract and module-owned enlistment setup. |
| [Public message Contracts](../../samples/Wholesale/modules/Inventory/Inventory.Contracts/StockReservationMessages.cs) and current [broker adapter](../../samples/Wholesale/WorkflowDemo/WorkflowBroker.cs) | Contracts contain versioned business messages; no public durable intake service is supplied. The current adapter validates delivery, commits receiver intake and then acknowledges RabbitMQ. Its transport setup knows contexts; this does not authorize business orchestration to access another module's private persistence. The proposed intake service is additional consumer-owned composition. |

Existing executable evidence remains historical, not fresh D2.3 test results:

- [Workflow inbox composition tests](../../src/Gloaming.Messaging/tests/WorkflowPostgresTests/InboxCompositionTests.cs)
  cover continuation/next-request/completion atomicity, rollback after an outbox constraint failure
  and equal outcomes under different deliveries without rerunning the continuation.
- [Workflow transaction tests](../../src/Gloaming.Messaging/tests/WorkflowPostgresTests/TransactionTests.cs)
  cover ambient rejection, rollback/fresh-state retry, competing turns and transaction/proposal guards.
- [Reservation tests](../../samples/Wholesale/WorkflowDemo.Tests/ReservationTests.cs) cover final refusal,
  compensation acknowledgment/refusal, retry identity, stale replies and the inability to resurrect
  released stock through a repeated reserve. [Failure tests](../../samples/Wholesale/WorkflowDemo.Tests/ReservationFailureTests.cs)
  cover atomic participant work and fresh processing after a failure in either module.
- [Worker process tests](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/WorkerProcessTests.cs) cover processor
  death/rollback and publisher death after transport confirmation, followed by recovery. These use
  the existing RabbitMQ-backed journey; they are not process-death proof of a local Contract adapter.

These findings support reuse of the current primitives and identify the missing consumer integration.
They establish neither shared cross-module commit nor a tested broker-free delivery path. The current
workflow has one pending request; parallel joins, deadlines, cancellation delivery and durable operator
signal deduplication remain outside its supported contract. See the
[workflow contract](../../src/Gloaming.Messaging/docs/workflows.md) and dated
[WF2 evidence](wf2-durable-continuations.md).

## Follow-up and extraction gate

If broker-free delivery is needed, the best next executable increment reuses the existing Sales-owned
reservation workflow and Inventory business Contracts, replacing only its transport with durable
public intake in one process. Demonstrate restart before/after intake commit, after participant commit
but before delivery acknowledgment, repeated semantic requests, reordered/stale replies, concurrent
reservations, permanent stock refusal and compensation refusal. State visibly whether intake or final
business completion was acknowledged. Keep current workflow/business behavior and workers; do not
introduce a new scheduler, cancellation protocol or state machine to prove transport composition.
Start consumer-owned; extract an adapter only if repeated mechanics emerge.
Retrying compensation may use a new delivery/workflow-command identity while retaining the
same business reservation identity. Refusing release of an unknown reservation is not a
cancellation tombstone that prevents a delayed reserve command from arriving later; that
race needs explicit process policy and proof if a later cancellation slice selects it.

A shared-transaction comparison is a separate useful sample when atomic visibility is a
requirement, with the native proof obligations above. It does not justify a universal
cross-module unit of work before that proof. D2.2 deferred reactions are not needed to
decide inter-module ownership here. EventReactions remains intra-operation preparation;
it does not become a durable inter-module protocol, scheduler or dependency graph.

Library finding: existing workflow and delivery obligations are the reuse candidates;
this research establishes no additional library mechanism. Template finding: no new setup
pattern is proven. Sample finding: process ownership, statuses, identity semantics,
compensation and transaction acceptance are consumer policy. Verification: primary-source
research only in this report's external comparison; no fresh runtime tests or native
cross-module transaction executions were performed by this research task.

## Review and verification

The roadmap merge preserved both D2 and incoming A1/A2 authorization scopes and was committed as
`5f90abe`. Its seven merged Markdown files passed local link and whitespace checks; commit hooks
ran commitlint and correctly skipped runtime checks for the documentation-only merge.

This D2.3 increment changes six documentation files: this report, its plan, the D2 umbrella,
the extraction plan, the roadmap and the workflow contract's deferred-feature link. Local links,
whitespace and conflict-marker checks pass. Archive verification confirms all 800 original files.
No C# or template files changed, so no new build or runtime test result is claimed. No new reusable
mechanism was proven. Review the recommendations and missing adoption proofs rather than treating
primary-source compatibility as executable consumer evidence.

The post-merge index manifest remains
`67234b3094ff6017dcb4548f7136857967685939c4f5eb0234d0d1de62d36b9c`.
Research edits are unstaged and uncommitted; the conflict-resolution commit is separate.
