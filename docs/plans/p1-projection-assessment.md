# P1: native queries and the Dispatch directory

Date: 2026-10-22. Assessment baseline: `790a8b7`. This is the bounded P1 decision
and proof boundary, prepared alongside the separately implemented B2 receiver population.
The [report](../reports/p1-projection-assessment.md) records executed evidence. No general
projector interface, new event-store lifecycle or shared module transaction is selected.

## Selected outcome

Retain native module-owned EF Queries/Filters for the current Sales, Inventory and
Purchasing read requirements. Select the B2 Dispatch customer contact/address directory
as the concrete asynchronous persisted view: Dispatch needs a complete searchable local
population, including customers that predate its subscription, and must serve that
population while Sales is unavailable. Sales owns the disclosed current representation;
Dispatch owns its storage, query policy, readiness and application of incoming state.

This requirement warrants a consumer projection even though current DTO shaping does not
warrant an event projection engine. B2 owns the single implementation and its population/
update protocol. P1 supplies the assessment and proof boundary; it does not create a second
receiver, an alternate feed, an offline population shortcut or a generic engine.

## Actual query requirements

| Consumer requirement | Current implementation and evidence | Decision |
| --- | --- | --- |
| Sales draft summary/search with current customer name, line count and total | [DraftOrderQueries](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderQueries.cs) joins Sales-owned draft/customer rows on Organization and customer identity; correlated line count/sum stays in the query. [Filters](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderFilters.cs) compose name/currency/total predicates before count/order/page. [C1 proofs](../../samples/Wholesale/IntegrationTests/Queries/QueryTests.cs) independently expect totals and tied-total ID ordering. | Native EF meets the existing same-module multi-aggregate read. There is no extra persisted view to synchronize. |
| Sales permission-complete detail/search/count/total/page | [DraftOrderAuthorization](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderAuthorization.cs) selects identified subject/Organization grants with EXISTS before shaping; [A2.1 proofs](../../samples/Wholesale/IntegrationTests/Queries/QueryTests.Authorization.cs) cover sparse and overlapping grants, collisions and revocation. | Keep the permission predicate in the owning module. A projection would still need an explicit authority/freshness policy. |
| Inventory available stock without replay | [StockPositionQueries](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionQueries.cs) joins one inline-state family to headers on the complete Organization/stream key; [StockPositionFilters](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionFilters.cs) computes on-hand minus reserved in JSONB before materialization. C1 observes SQL and rejects stored-event reads. | Native mapped-state filter/header validation meets this requirement. This is not a join across independently owned business streams/modules. |
| Purchasing point summary | [PurchaseOrderQueries](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderQueries.cs) loads one checked inline state and calculates code/currency/line count/total in memory. [Existing inline-view proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/AppendTests.InlineViews.cs) cover line replacement and derived summary. | No additional persisted summary is needed for this point query. Server-side list filtering/sorting/counting over JSON line totals remains unproven. |
| Advisory Inventory availability followed by Sales draft work | [CqrsJourney](../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/CqrsJourney.cs) calls typed Contracts in separate operations; Sales SKU validation also calls Inventory Contracts. | Existing bounded composition is appropriate for advisory use. It grants neither stock reservation nor a common snapshot/transaction. |
| Dispatch complete customer contact/address directory that serves while Sales is unavailable | B2's selected receiver requirement crosses Sales ownership and has an availability/population obligation that the preceding reads do not have. | Persist the disclosed state in Dispatch and query it locally. Prove B2's asynchronous consistency and population boundary explicitly. |
| Historical metrics discarded from current state | No named active metric requires these in the assessed consumers. Current stock state, for example, retains only the latest delivery reference. | Do not invent a metric to justify an engine. Future historical requirements need retained facts/public history sufficient for that exact metric. |

## Ownership, consistency and completeness

[Module Contracts](../../samples/Wholesale/modules/README.md) expose immutable results rather
than peer rows or IQueryable. Sharing a PostgreSQL server or even connection-string text
does not give Sales a supported query provider over Inventory/Purchasing/Dispatch private
contexts. No proposed join should quietly add those rows to SalesDbContext or grant Sales
access to private peer tables. Public native contexts exist for composition and tooling;
that exception does not transfer business ownership.

The tested Sales join is useful positive multi-aggregate evidence within one module. It
does not establish arbitrary grouping translation or joins across private contexts. The
Inventory join verifies a state's header association, rather than a second independently
owned aggregate. Purchasing currently materializes one state before computing its total;
its point-summary proof cannot certify a future SQL total filter.

Count and page execute as two SQL statements. C1's concurrent insert and A2.1's grant
revocation tests demonstrate read-committed drift and the caller's repeatable-read option.
Deterministic total/ID ordering does not freeze later page requests. That native Sales
snapshot cannot be extended by inference to an earlier Inventory Contract call. Likewise,
Dispatch can offer a coherent snapshot of its local directory without claiming that all
Sales customers are observed at the same instant or that directory state authorizes a
Sales business action.

A sparse on-demand Sales lookup can supply one missing customer's current representation.
It cannot establish that Dispatch has every searchable customer, preserve local searches
during a Sales outage, or discover deletions by omission from an arbitrary partial page.
Directly querying Sales for every Dispatch search would leave that availability requirement
unmet. A fixed offline copy would leave the selected ongoing-update requirement unmet.

## B2 proof contract

The following obligations apply to the selected directory. They define the acceptance
boundary; they are not claims that this assessment implemented or executed B2. Its own
report must identify the concrete protocol and passing proofs.

| Concern | Required bounded proof |
| --- | --- |
| Public state and identity | Sales explicitly discloses the contact/address shape keyed by Organization/customer. A source-owned revision applies to that whole disclosed representation, including any address change. The receiver does not infer it from schema version, delivery identity or event-stream version. |
| Updates and ordering | Complete replacements plus explicit tombstones converge when duplicated, reordered or overlapped with population. Different customer keys need no global order; old revisions cannot overwrite new state or resurrect tombstones. Equal-revision disagreement needs a defined rejection/integrity policy. |
| Population overlap | Customers existing before subscription appear without waiting for their next edit. Updates committed before, during and after capture cannot be lost between export and durable delivery. A precommit timestamp/sequence maximum alone is insufficient; B1's late-commit finding still applies. |
| Receiver concurrency | Real PostgreSQL races for the same key, including missing-row insert and update/delete overlap, preserve the selected revision. Different keys can progress independently. A later shared directory-total row would need its own cross-key concurrency proof. |
| Effect/progress atomicity | Applied row/tombstone, durable receipt and any population progress that allows skipping work commit consistently in the receiver transaction. Crash before commit permits retry; crash after commit before acknowledgement permits harmless repeat. |
| Checkpoint/restart | Define whether a position is a fixed export page, durable subscription receipt or source change boundary. Interrupt and resume population/update processing without skipping captured work. A message ID or source revision is not automatically a global checkpoint. |
| Serving and cutover | Incomplete population is distinguishable from a complete empty result. Demonstrate the selected readiness/cutover rule, same-tenant filtering/count/order/page and independent query use while Sales is unavailable. State the freshness limit rather than imply instantaneous source agreement. |
| Deletion and retention | Tombstones survive the window in which old population/update state can reappear. Retained deliveries cover interruption and late population; expired history needs explicit re-population/reconciliation or failure. Inbox deduplication expiry cannot make stale public state current again. |
| Versioned rebuild | State whether replacing the directory/reducer while serving is supported. Existing single-stream aggregate regeneration is not this operation. If online shadow population/cutover is omitted, record it as unsupported rather than claim it from initial bootstrap. |

There is no need for a global private-event scanner, leader election or multi-stream inline
write in this directory merely to populate independent keyed replacements. B2 must choose
and prove the smallest actual protocol. If it deliberately chooses one processor, state
that deployment contract; it does not waive same-key race/retry obligations in supported
paths. Using existing inbox/outbox mechanics transfers only their documented guarantees,
not the new state merge or export-overlap guarantee.

## Extraction and example disposition

The existing repeated native query shape is module-local: Sales can reuse ReadView and
Filters without a library query abstraction. Inventory's JSON extraction/header validation
and Purchasing's materialized summary have different obligations. **No new reusable
mechanism is proven by this assessment.**

The Dispatch merge, revision meaning, tombstone policy, public export and readiness belong
to the consumer initially. After implementation, compare any repeated technical population/
checkpoint/transaction work across a second real consumer before proposing extraction.
Simple mechanics can qualify; an arbitrary complexity threshold is not required. Existing
Messaging mechanisms remain useful independently of event sourcing, and this state-stored
producer/receiver is useful evidence of that independence.

Keep independently adoptable family examples such as EventStorageDemo and MessagingWorkerDemo:
their narrow dependencies test adoption outside the Wholesale module layout. Keep one
coherent Wholesale business sample for cross-module stories. Its native Sales queries,
Inventory advisory/authoritative distinctions and Dispatch directory should share the same
module ownership and public language. Finite CqrsDemo/EventPersistenceDemo/WorkflowDemo entry
points can remain focused executable proofs of that model, with explicit setup differences;
they should not become separate competing customer or inventory models.

Consolidation is a separately reviewed inventory/relocation change. This assessment moves
no files, removes no demonstrations and introduces no product frontend or template preset.
It recommends keeping proof entry points where they establish real dependency/failure
boundaries, then consolidating duplicated setup/docs once the coherent Wholesale hosting
and migration scope is concrete. T1's event-free Catalog output is unchanged.

## Completion boundary

P1 resolves which current queries need persisted state and selects Dispatch as the concrete
asynchronous view coordinated with B2. It does not certify all deferred event-sourcing
projections. Secondary inline views, many-stream shared aggregates, private-event committed
ordering, historical metrics, general daemons and online projection-version cutover still
need an actual consumer and their own proofs. The current one-inline-state event-store and
explicit single-stream rebuilder retain their existing supported contracts.
