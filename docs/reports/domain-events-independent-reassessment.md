# Domain events: independent reassessment

Current review entry: [EventReactions consolidation](../reports/event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Research date: 2026-10-15. Scope: primary-source research and design analysis,
independent of this repository's runtime implementation and sample business rules.
No runtime implementation was inspected and no executable proof was performed.
**Sourced finding** identifies a source's position; **Analysis** identifies this
report's inference or recommendation. Examples are hypothetical requirements,
not claims about existing application behavior.

Subsequent owner decision: retain bounded atomic intra-module dispatch, prefer explicit orchestration
for complex dependencies, and preserve Raise capture order across participating sources. The
[implementation and workflow durability review](d2-1e-local-order-and-workflow-durability.md) records
that follow-through separately from this source-only reassessment.

## Findings

Domain events have a legitimate use connecting aggregates within one module.
They can express a business occurrence and separate its reactions from the
object that produced it. An event does not itself select a consistency model:
an in-memory reaction before commit, a durable message processed after commit,
and an explicit application workflow have different failure semantics.

**Sourced finding:** Microsoft's DDD guide explicitly supports deferred local
events dispatched before commit with related aggregate changes in one
transaction. It also describes after-commit delivery with eventual consistency.
Jimmy Bogard's original deferred pattern records events on entities and invokes
handlers before committing. Consequently, strong consistency does not require
manual orchestration; precommit domain-event dispatch is an established option.
See [Microsoft's domain-event guide][ms-domain] and
[Bogard's original deferred pattern][bogard].

**Analysis:** None of these sources establishes a need for a reusable framework
in this repository. Start with a specific business requirement and failure
contract. A small explicit application service can be the complete solution.
Choose a dispatcher when decoupling meaningful reactions pays for the extra
control flow; choose durable messaging when a reaction must survive independently
of the initiating request. Do not add a saga solely because two aggregates exist.

## What the original sources establish, and where they disagree

**Sourced finding:** Udi Dahan's 2009 example represents an interesting domain
occurrence explicitly and invokes handlers on the initiating thread, within the
transaction. He warns against blocking SMTP or web-service calls there and
suggests one-way messaging for those effects. The purpose is keeping services
and repositories out of entities while making domain language visible. This is
a design motive, not an automatic reliability guarantee. His later comments on
that same page favor NServiceBus sagas and warn about static dispatch with
async/await; the article should not be treated as timeless endorsement of a
particular static dispatcher. [Dahan, including dated author comments][dahan].

**Sourced finding:** Bogard separates recording an event from dispatching it,
primarily to isolate domain tests and defer side effects to the transaction
boundary. He also acknowledges direct arguments when immediate collaboration is
what the model needs. [Bogard][bogard].

**Sourced finding:** Vaughn Vernon recommends small consistency boundaries and
eventual consistency for rules spanning aggregates. Subscribers load other
aggregates and perform separate transactions, retrying on contention. This is a
different default from Bogard's related changes in one transaction. His essay
also uses business responsibility to distinguish transactional requirements from
work the system can finish later, and permits justified exceptions involving multiple
aggregate instances in one transaction. [Effective Aggregate Design, Part II][vernon].

**Analysis:** “DDD requires asynchronous events between aggregates” overstates
consensus; “events are unsuitable for required atomic reactions” does too.
Repeated atomic collaboration is a reason to revisit aggregate boundaries,
contention and the actual invariant. It does not, by itself, prove that either
one giant aggregate or a distributed workflow is the better model.

## Concrete same-module example: library loans and patron borrowing accounts

**Hypothetical requirements and assumptions:** One Lending module owns `Loan`
and `PatronBorrowingAccount`. A loan has its own lifecycle and return history;
an account controls concurrent borrowing and suspension for one patron. A
patron may have at most five active loans. A checkout response may report
success only when both an active loan and its account reservation are committed.
Both aggregates use one relational database and one unit of work. All checkout
entry points must enforce the same rule. An account concurrency token or
equivalent database coordination prevents concurrent requests from reserving
the same final slot. Notifications may arrive later.

The following are alternatives for these assumptions, not implementation plans:

| Approach | Checkout path | Result and cost |
| --- | --- | --- |
| Explicit application orchestration | Load account; reserve a slot; create loan; save both once | Directly visible ordering and failure behavior; each entry point must reuse the operation |
| Local precommit event | `Loan.CheckOut` records `LoanCheckedOut`; its local handler loads the account and reserves a slot; save all changes once | Same atomic outcome if every required handler runs and failures prevent commit; reaction is separated from the loan |
| Durable deferred reaction | Commit a pending checkout plus a durable request; another handler reserves a slot and confirms or rejects checkout | Survives process loss but changes the business contract to pending/confirmed; extra state and operational work |

**Analysis:** The second approach is meaningful when checkout can originate in
multiple application flows and the reaction is a stable domain rule. The first
is often easier when there is one short operation with a known sequence. An
event handler is still orchestration somewhere; separation trades visible local
control flow for discoverable event-to-handler relationships.

In the precommit alternative, an in-memory event may disappear safely on process
failure **before commit** because its associated database changes must disappear
too. If the commit completed, both persisted changes must already be complete.
This requires coordinated persistence, not simply awaiting arbitrary handlers.
It does not protect an email, HTTP call or independent database write made by a
handler. After a failed attempt, discard or deliberately rebuild the unit of work;
cleared event buffers and mutated tracked objects are not automatically restored
by a database rollback. Response loss after commit also requires command
idempotency if clients may retry.

**Sourced finding:** EF Core ordinarily makes the changes in one `SaveChanges`
atomic when the provider supports transactions. Multiple contexts can share a
relational transaction when they share both `DbConnection` and `DbTransaction`.
Manual transaction management must account for retry execution strategies.
[EF Core transaction documentation][ef-transactions].

**Analysis:** Precommit dispatch can happen before the database transaction
actually begins. Its database reads, concurrency checks and eventual writes
still need an appropriate isolation/concurrency design. “Before SaveChanges”
alone does not serialize two checkouts or make stale reads safe.

## Ordering: routing is not a dependency graph

### An ERP-shaped A/B/C example

**Hypothetical requirements:** A Warehouse Receiving module owns a `GoodsReceipt`, independently
managed SKU/location `StockPosition` aggregates and a `DockAppointment` with its own booking lifecycle.
Posting a receipt must increase accepted stock and release its occupied appointment atomically.
These assumptions deliberately select one database/unit of work; they are not universal warehouse rules.

| Role | Meaningful event subscriptions | Required reaction |
| --- | --- | --- |
| A: GoodsReceipt | Produces ReceiptPosted (event A), GoodsAccepted (event B), AcceptedQuantityCorrected (event C), ReceiptNoteChanged (event F) | Validates its receipt and records accepted decisions |
| B: StockPosition | GoodsAccepted and AcceptedQuantityCorrected | Applies signed quantity changes under stock rules and concurrency control |
| C: DockAppointment | ReceiptPosted | Completes the identified appointment and releases its slot |

Posting emits ReceiptPosted and GoodsAccepted for its lines. A note edit can produce event F in the
same unit of work without stock or appointment handlers subscribing to it. Event C is handled only
when a correction actually occurs. Neither receiver is a child of every receipt: a stock position
is shared by many receipts/issues, while an appointment is booked/rescheduled independently.

Local handlers can stage all three changes before commit. If the required stock update fails,
the receipt and appointment changes must not commit. Explicit application calls can enforce the
same outcome and may be clearer for a small fixed workflow. If dock release may lag, retaining that
reaction durably is another contract. If a proposed receiver only stores a derived display total,
it may be a projection rather than an aggregate; do not invent domain entities to justify dispatch.

The event pattern centralizes the reaction across receipt entry points, but registration does not
prove all required policies were registered. Adding a required subscriber also changes the producer's
transaction failure conditions and potential contention. Scenario tests still establish the rule.

### Why source order needs a separate contract

**Analysis:** A table mapping event types to handlers is a routing table. It
does not infer that one handler's output must precede another handler's input,
prove termination, identify missing required business policies, or establish a
chronological order between independently buffered aggregates.

Consider account A recording `SlotReserved`, loan B recording `LoanActivated`,
then account A recording `SlotReleased`. Per-aggregate buffers hold:

```text
A: SlotReserved, SlotReleased
B: LoanActivated
```

Flattening A then B yields reserve/release/activate; B then A yields
activate/reserve/release. Neither reconstructs reserve/activate/release. FIFO
inside each buffer is insufficient. Sorting timestamps or event names does not
recover missing causal information. A shared capture sequence can preserve
recording order within one operation, but that is an additional mechanism with
its own concurrency assumptions; it does not produce a global distributed order.

If activation requires reservation, make that dependency explicit: call the
steps in order, return a domain decision, emit activation only after reservation,
or model a stateful workflow. Handler registration order is a fragile substitute.
Draining recursively raised events also needs a termination/failure policy; an
iteration limit prevents a hang but does not make a cyclic business rule correct.

### Current-state reconciliation versus historical transitions

**Analysis:** A handler told “account eligibility may have changed” can reread
the authoritative final state and recompute eligibility. Coalescing repeated
notifications may then be appropriate. It must read the intended state: a SQL
query before save can miss unsaved tracked changes; a later query sees committed
state as of that later read, not necessarily the state at event creation.

Recomputation is not automatically monotonic or immune to stale writes. With
overlapping workers, an older computation can overwrite a newer result. Use
version checks, serialization or another concurrency strategy. A monotonic
operation such as adding a known fact to a set has different properties from
replacing a value computed from mutable state.

By contrast, `LoanCheckedOut` followed by `LoanReturned` may both matter for
fees, audit or external notifications even if the final state is returned. A
handler rereading the loan cannot reconstruct those transitions. Carry the
required immutable facts, aggregate identity and version/sequence; define
duplicate, gap and out-of-order behavior. Avoid treating an entity reference as
an immutable snapshot. The chosen business meaning determines whether to retain
every transition or publish only a final-state notification.

## Crossing module boundaries with isolated persistence

**Sourced finding:** Microsoft's saga description distinguishes choreography
(participants react to messages) from orchestration (a controller tracks progress
and directs participants). Both compose local transactions; compensation is a
later business operation, not database rollback. Its guidance highlights absent
cross-service isolation, monitoring and idempotency needs.
[Microsoft saga pattern][ms-saga].

**Analysis:** These choices address different dimensions. An outbox atomically
records the sender's intent alongside its own data. A saga coordinates progress
and recovery across steps. Neither makes independent module commits one atomic
transaction; both orchestration and choreography need reliable handoff if work
must survive crashes.

| Choice | Persistence assumption | Appropriate use and main obligation |
| --- | --- | --- |
| Durable event choreography | Each module commits its own data and outgoing work; consumers durably handle it | A few independent reactions, tolerable delay; define idempotency, failure ownership and visibility |
| Durable orchestration/process manager | Coordinator state and outgoing commands are committed reliably; participants retain their own transactions | Branching steps, deadlines, reservations, rejection/compensation; persist correlation and progress |
| Shared native SQL transaction through module contracts | Participants can enlist in the same supported native transaction | Immediate all-or-nothing result with accepted deployment/storage coupling; prove transaction participation |

For example, Lending and Membership may own separate schemas and contexts while
sharing a SQL database. Their public application contracts could join a
host-coordinated transaction without either module accessing the other's tables.
That preserves code/data ownership while intentionally sharing a transaction
boundary. The transaction plumbing can be infrastructure; it need not expose
EF types in business contracts. Ownership isolation does not imply independent
transactional failure isolation.

However, “same SQL server,” “same connection string,” “same process,” or “same DI
scope” does not prove enlistment. Separate database services cannot share this
native transaction by convention. Shared schema/context ownership, permissions,
connection management and commit authority must be explicit. A module that
commits early or performs external I/O defeats whole-operation rollback.
Cross-database/provider and distributed transaction support are separate facts
requiring concrete verification. A shared native transaction also couples locks,
availability and future extraction. Do not invent a saga merely to avoid an
acceptable local SQL transaction, or promise a local transaction when persistence
is genuinely independent.

## What Wolverine actually provides

The following describes current public documentation and upstream `main` source
accessed on the research date. It is not a result against a pinned package,
repository dependency or production configuration.

**Sourced finding:** Wolverine's EF domain-event integration gathers entity
events and enqueues outgoing messages within the initiating transaction.
Durability depends on configuration. Its documentation marks the feature as
introduced in 5.6 and warns that lightweight-handler scraping did not work before
6.41. It also documents scoped-buffer limitations for factory-created tenant
outboxes. A domain-event publishing API name therefore does not imply identical
behavior across versions or execution paths.
[Publishing Domain Events][w-domain].

**Sourced finding:** The examined `EfCoreEnvelopeTransaction.CommitAsync`
scrapes events, persists tracked envelope changes when applicable, commits the
database transaction, then flushes outgoing messages. This is direct source
evidence of an outgoing messaging boundary, not proof that subscriber reactions
run inside the originating aggregate's transaction.
[Upstream source, mutable `main`][w-source].

**Sourced finding:** Wolverine requires message persistence, durable endpoints
and transactional integration/outbox usage for the reliable store-and-forward
path. Local queues can be durable; a separate broker is not required. Buffered
or inline endpoints are also supported, so adding the framework or calling
publish does not alone establish recoverable delivery.
[Wolverine durability documentation][w-durable].

**Sourced finding:** Cascading handler results are sent after the original
handler succeeds. Wolverine also supports inline `InvokeAsync` calls, but its
modular-monolith tutorial recommends asynchronous intermodule communication in
most cases. These are distinct execution paths.
[Cascading messages][w-cascading], [modular monolith tutorial][w-modular].

**Sourced finding:** Current EF middleware documentation permits one
transactional `DbContext` per handler chain and requires explicit selection when
multiple contexts are present. This does not automatically compose separate
module contexts into one atomic write. [EF transactional middleware][w-ef].

**Sourced finding:** Wolverine sagas persist state between messages and
coordinate multistep processes. Partitioned messaging can serialize handling by
an explicitly selected group; its documented scopes and topology matter.
[Saga documentation][w-sagas], [partitioned sequential messaging][w-order].

**Analysis:** Wolverine can remove substantial outbox, retry, local durable queue
and process-manager plumbing when those capabilities are required. It does not
decide what an event means, invent a business dependency graph, recover
chronology already lost by independent aggregate buffers, or automatically make
module state globally atomic. Serial processing in delivery order is not proof
of source causal order. A persisted saga still needs reliable incoming/outgoing
handoffs; durable state alone cannot recover a command that was never durably
recorded. Neither it nor an outbox makes arbitrary external effects exactly once.

## Decision and evidence boundaries

**Recommendation:** For one short, atomic same-module operation, begin with
explicit orchestration or a small precommit dispatcher according to which makes
the business rules clearer. Both are valid candidates. Use durable deferred
local reactions when completion after the initiating commit is an actual
requirement, accepting pending states and recovery operations. Use a process
manager when a workflow must remember progress, deadlines or compensation;
outbox delivery remains complementary infrastructure.

Keep event meaning, required reaction, transaction ownership, concurrency policy,
ordering key, retry semantics and compensation in the consuming business module.
Potential reusable mechanisms are capture/dispatch and persistence/delivery
plumbing, but extraction requires repeated consumer evidence. A generic handler
registry is not evidence that a workflow engine is needed.

Library finding: existing primary sources establish credible implementation
options, not an unfilled reusable-library requirement. Template finding: no new
template setup is justified without a selected persistence/delivery path. Sample
finding: the lending scenario is a thought experiment with explicit assumptions,
not executable consumer usage. **No new reusable mechanism was proven.**

Verification performed: read the repository workflow; browsed original-author,
Microsoft and Wolverine primary sources; inspected the named upstream source
file; preserved the Git index. No C# implementation, database experiment,
crash/restart exercise or package-version proof was performed. In particular,
atomic rollback, aggregate concurrency, delayed/reordered/duplicate transitions,
tenant outbox scope and a shared transaction across module contracts remain
proof obligations if an implementation is chosen. Historical repository proofs
are neither re-run nor promoted to fresh evidence by this report.

[ms-domain]: https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation
[bogard]: https://lostechies.com/jimmybogard/2014/05/13/a-better-domain-events-pattern/
[dahan]: https://udidahan.com/2009/06/14/domain-events-salvation/
[vernon]: https://www.dddcommunity.org/wp-content/uploads/files/pdf_articles/Vernon_2011_2.pdf
[ef-transactions]: https://learn.microsoft.com/en-us/ef/core/saving/transactions
[ms-saga]: https://learn.microsoft.com/en-us/azure/architecture/patterns/saga
[w-domain]: https://wolverinefx.net/guide/durability/efcore/domain-events.html
[w-source]: https://github.com/JasperFx/wolverine/blob/main/src/Persistence/Wolverine.EntityFrameworkCore/Internals/EfCoreEnvelopeTransaction.cs
[w-durable]: https://wolverinefx.net/guide/durability/
[w-cascading]: https://wolverinefx.net/guide/handlers/cascading
[w-modular]: https://wolverinefx.io/tutorials/modular-monolith.html
[w-ef]: https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html
[w-sagas]: https://wolverinefx.net/guide/durability/sagas
[w-order]: https://wolverinefx.net/guide/messaging/partitioning.html
