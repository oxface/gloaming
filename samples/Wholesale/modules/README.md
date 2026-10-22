# Wholesale consumer modules

The HTTP sample composes three populated module implementations through separate Contracts
projects. The native event-history executable also composes Purchasing. These are
consumer-owned sample/template projects, not reusable technical libraries.

The separate [compensating reservation workflow](../Worker/Broker/compensating-reservations.md)
adds Inventory-owned reservation receipts and available-stock decisions, plus Sales-owned
two-line process state and compensation attempts. Both contexts commit independently using
their existing inbox/outbox; implementations still communicate only through Contracts.

The [local workflow executable](../Worker/Local/README.md) runs the same business process
without RabbitMQ. Inventory and Sales expose typed durable intake Contracts whose successful calls
mean committed receipt. Each intake uses a fresh module-owned scope; relays never access receiver
contexts. Sales.Contracts explicitly references Inventory.Contracts for Inventory-owned outcome types.

| Module | Ownership | Persistence |
| --- | --- | --- |
| Access | Global application users, exact external identities, canonical Organizations, current-membership admission and authorized membership suspension/reactivation. | Schema `access`, native AccessDbContext, separate migration history; global admission queries run before tenancy exists. Scoped administration owns its native transaction and accepted-change audit. |
| Sales | Versioned customer profile edits, one demonstration address per customer, relational draft orders/lines, submitted-order allowances and durable stock-issue request progress. | Schema `sales`, native SalesDbContext, separate history and owned inbox/outbox; explicit tenant ownership and native transactions. |
| Inventory | Availability reads, stock-position history and inline state, opening and receipt commands within the selected Organization. | Schema `inventory`, native InventoryDbContext, separate history; ordinary queries use the registered E2 ownership filter. |
| Purchasing | Purchase-order history, inline aggregate state and query-derived summary, drafting and line replacement. | Schema `purchasing`, native PurchasingDbContext, separate history; explicit tenant ownership. |

Each implementation references its own Contracts. None references a peer implementation
or the HTTP host. Contracts contain domain keys, query/command interfaces and immutable results;
they expose no EF, HTTP or technical context types. Rows and query implementations are internal.
Inventory stores the canonical Organization key without a cross-module database FK.

The [host](../Api/DemoComposition.cs) registers each native DbContext/provider
explicitly, calls `AddAccessQueries()` / `AddInventoryQueries()` / `AddCustomerProfiles()` to bind internal
implementations, and separately registers its HTTP resolvers. Business
[endpoints](../Api/Endpoints/OrganizationEndpoints.cs) call only business
Contracts. Actor/provider mapping, Organization admission metadata and selection helpers
live in the host's [HTTP integration](../Api/HttpIntegration/OrganizationTenantResolver.cs).

[Access administration](Access/README.md) adds `AddMembershipAdministration()` and narrow
consumer Contracts. The global registry remains independent of current tenancy for identity
lookup; administrative operations explicitly scope authority/targets, protect revision and
Organization, and use current active membership. Its grant supplies no business-data permission.

Public native DbContexts/configuration utilities are available to composition, native EF
tooling and finite setup. This exception keeps saves, migrations and transactions visible;
it does not make persistence types part of the business Contracts. Module seed helpers stage
rows only. Their caller establishes tenancy where needed and explicitly saves/commits.
Raw SQL and privileged native EF bypasses remain consumer responsibilities.

Inventory's `IStockCatalog.ReadAsync(sku, cancellationToken)` returns a result or null in the
current tenant. It accepts no caller-selected tenant, uses no filter bypass, saves nothing,
and propagates database faults/cancellation. Missing or tenantless context rejects access.
The schema permits the same SKU in multiple Organizations through an Organization/SKU unique
index. Availability is a small state-stored demonstration view; reservation commands and an
authoritative event-sourced stock model are not established here.

[Host setup and endpoints](../Api/README.md),
[the E3.4 plan](../../../docs/plans/e3-4-persisted-business-ingress.md) and
[the report](../../../docs/reports/e3-4-persisted-business-ingress.md) record adoption, proofs
and remaining limits. [E3.5](../../../docs/reports/e3-5-profile-mutation.md) adds the Sales mutation.

Sales's [Contract](Sales/Sales.Contracts/ICustomerProfiles.cs) returns a profile, or a typed
Updated/NotFound/Conflict outcome. Read and change require established tenancy. Change validates
IDs, text lengths and version range, uses the caller's expected version in the native UPDATE,
saves customer then address, and commits its own native transaction before returning Updated.
No actor/tenant is accepted from the request body. HTTP authorization and antiforgery remain
host obligations; trusted non-HTTP callers own admission. Discard the context after failure;
there is no automatic retry/rebase or shared ambient-transaction contract.

Materialized template/CLI output remains E10.

## DDD sample patterns

[DddDemo](../IntegrationTests/Scenarios/Ddd/README.md) compares Sales's immutable state-stored customer profile with
Purchasing's existing event-sourced purchase order. Sales owns a validated profile-details
value and builds a complete replacement candidate before changing either native row. Finite
setup constructs a valid version-1 profile before tracking; it is not a production creation API.
Invalid input leaves accepted state untouched. A mismatched observed profile version returns
Conflict before staging; native EF still detects commits racing after the read. Trusted-human
admission and the service-owned customer/address/audit transaction are retained.

Purchasing keeps complete-batch candidate validation and pending-event bookkeeping through
its existing optional event library; the caller still saves/commits. These are different
consumer recipes, not a shared aggregate hierarchy. Business Contracts and migrations are
unchanged. The [D1 report](../../../docs/reports/d1-ddd-sample-patterns.md) records fresh proofs
and the decision that no new reusable mechanism or template preset was established.

## Required local submission reactions

[DomainEventsDemo](../IntegrationTests/Scenarios/DomainEvents/README.md) adds draft submission and a separate Sales-owned
customer order allowance. An accepted submission carries a private domain fact; its required
handler invokes the allowance's reserve decision. Owning-context SaveChanges stages that work
before validation, and both aggregates persist in the caller's native transaction. Explicit
staging is available through the library when needed. Gloaming's registered adapter and ordinary
typed Sales handler replace the original Sales-only coordinator. This is local transactional handling, without event sourcing
or broker delivery. Different orders compete on the allowance version; direct SQL remains outside
the protocol. [The current cleanup report](../../../docs/reports/d2-1c-domain-event-integration-simplification.md)
records registration, lifecycle and the removal of default mirrored-write safeguards. Allowance release and durable deferred work
remain separate capabilities.

## Rich state-stored Sales draft

[RichAggregateDemo](../IntegrationTests/Scenarios/RichAggregate/README.md) adds a Sales-owned draft order with relational
line children and an immutable total limit supplied by a trusted caller. The root controls child
add/change/remove and validates the complete candidate. A customer identity references the separate
Sales profile; a catalog SKU references Inventory through its Contracts, with no private peer query
or cross-module FK. The summary joins current customer information without copying it into the root.

Draft commands stage in the caller's already active native Sales transaction. Every accepted batch,
including a child-only change, advances the root version once. Explicit single-query graph loading,
root-version UPDATE predicates and domain decisions preserve the intended command path;
explicit deletion staging owns child removal. The default tracked-save guard is removed.
Direct tracked writes and bulk/native SQL bypass the aggregate protocol.
The forward migration adds only `draft_orders` and `draft_order_lines`, with same-tenant root/child
and customer FKs. Prior migrations and customer/profile/workflow behavior remain intact.

This is a state-stored domain example, not an event stream, projection engine, reservation workflow
or domain-event dispatcher. The existing two-line reservation workflow is a separate business
process and is not automatically started by draft creation. [The D1.1 report](../../../docs/reports/d1-1-rich-state-stored-aggregate.md)
records fresh proofs, scoped guarantees and the decision that no new reusable mechanism was proven.

## Closed integration-feed assessment

B1 recorded retention and public-contract constraints without selecting a durable example
for this sample. The [assessment](../../../docs/reports/b1-retention-and-contract-assessment.md)
and dated results are retained; experiment code and patch artifacts are removed. Normal
Sales profile writes and setup have no feed gate, feed tables or feed Contracts.
Revisit transfer/replay only for a concrete consumer need.

## Durable stock-issue progress

[WF1 executable usage](../Worker/Broker/README.md) adds a Sales-owned request that sends the
existing Inventory `IssueStockV1` command and resolves its success/refusal reply. Sales owns
request identity, deadline, outcome matching and semantic idempotency; Inventory owns the stock
decision. Sales references Inventory.Contracts and never Inventory's implementation or private
state. Each module commits independently through its own typed context and inbox/outbox.

The [business Contract](Sales/Sales.Contracts/IStockIssueRequests.cs) exposes explicit start
and persisted reads. Starting commits the request and command together; reply processing
commits progress with inbox completion. A separate bounded deadline operation flags
NeedsAttention without declaring remote failure. Late replies can still resolve it. Native EF
request version predicates arbitrate replies/deadline scans in different local transactions.

The sample host establishes trusted contexts, owns broker admission/routing/acknowledgement,
and explicitly runs setup. The current finite receivers admit known Organizations; that is
not production sender authentication or a replacement for business authorization. Compensation,
cancellation and poison recovery require separate consumer policies/proofs.

## Event-history reads

[E5.2.1](../../../docs/plans/e5-2-1-native-event-history.md) adds stock-position history to
Inventory and introduces Purchasing/Purchasing.Contracts for purchase-order history.
Purchasing owns schema `purchasing`, a native PurchasingDbContext and a separate migration
history. Both module implementations explicitly adopt Events.Serialization, Events.History
and the existing EF tenant-ownership utility; neither uses messaging or ActorIdentity.

A stock position records opening at a stock-item/location pair and positive receipts in its
base unit, retaining the latest delivery reference. The read model reports on-hand quantity;
reservation/adjustment commands and durable business-key uniqueness are not implemented.
A purchase order records its code, supplier reference and currency, with positive line
quantities and nonnegative unit prices. A later line fact for the same item replaces that
line. The initial commands cover drafting and those line changes; a complete purchase-order
lifecycle is not implemented.

IStockPositionHistory and IPurchaseOrderHistory expose current, version and recorded-time
queries through immutable business results. Their implementation rows and codecs stay internal.
Public native contexts and finite HistorySeed helpers remain composition/setup exceptions,
not business Contracts or production append APIs. The caller establishes tenancy and saves.

Headers use `(organization_key, id)` primary keys; events use an owned event identity and an
owned stream-position unique index. A composite owner/stream foreign key prevents cross-tenant
references. Positive stream/event/schema versions are database constraints. Native migrations
and storage/ownership choices remain editable consumer/template source. [E5.3 registration](../../../docs/reports/e5-3-event-storage-registration.md)
now supplies the repeated technical mapping through an explicit call in each owning context;
there is no context discovery or automatic schema population. Existing snapshots/migrations
were preserved by E5.3. E6.1 adds native forward migrations for the three required inline views;
existing history and availability data remain intact.

[The native executable](../IntegrationTests/Scenarios/EventPersistence/README.md) exercises both modules independently
of HTTP and messaging. Its read consistency assumptions and error boundaries are documented
there. The existing HTTP application continues to use Inventory's state-stored catalog and
now applies the forward Inventory migration; it registers no history queries. Sales and Access
retain their existing persistence behavior. Owning Inventory's new event features introduces
its explicit event-library references; selecting only its catalog registration does not remove
those assembly dependencies.

## Explicit event commands

[E5.2.2](../../../docs/reports/e5-2-2-native-event-append.md) adds module-owned command decisions
and staging. IStockPositionCommands opens a position and stages positive receipt batches;
IPurchaseOrderCommands drafts an order and stages line batches. Their DTOs contain explicit
stream identity, business values and expected version, without tenant/actor or EF types.
Consumers register commands explicitly alongside history queries and native contexts.

Changed is proposed state only. The caller must start the module's native transaction before
command preparation, save and commit explicitly, and handle both preflight NotFound/Conflict
and save-time failures. Internal decisions deliberately produce typed events, load checked inline
decision state, evolve the complete candidate and encode all required rows before tracking. Native
EF original-version predicates and owned keys arbitrate competing writers. Headers/envelopes
commit together; no worker lock, hidden retry, automatic event collection or publishing is added.

The supported recipe is one staged batch per stream per context. Native tracked headers detect
repeats before and after save; no separate command-lifecycle set is stored in the DbContext.
Manual clearing/detaching tracking mid-operation is outside this recipe. Different
streams can share the caller's native module transaction. Discard the whole context and proposal
after failure/rollback; a fresh operation may decide again. Public native AppendFailures helpers
recognize only stream/required-view concurrency and known owning-schema key/position constraints;
catalog concurrency and unrelated constraints remain faults.

Stock-item/location uniqueness and purchase-code uniqueness across different streams remain
unproven. Availability still is not an event projection. Required inline views are implemented;
repair, audit and messaging remain later explicit participants. No new Foundry library mechanism was
needed for these writes; the native setup and protocol are editable template candidates.
The later E5.3 utility extracts technical model registration only; it does not take over
command decisions, event collection, saving or transaction ownership.

## Inline decision state and committed queries

[E6.1](../../../docs/plans/e6-1-inline-decision-state.md) adds module-internal state and explicit
projectors. Stock Position maintains one aggregate-shaped write view; Purchase Order maintains
its write view and a separate summary. Native key/ownership/version mappings, JSON state shapes,
summary reduction and required-view policy are consumer source. Commands no longer depend on
history readers. IStockPositionQueries/IPurchaseOrderQueries return committed views; the existing
History Contracts still replay facts explicitly. Missing/behind required views fail, while a
newer view observed after an older header is concurrency. There is no ordinary-write repair.

Forward migrations create empty view tables and preserve existing events; they do not backfill
the live-history fixture streams. The executable's command-created streams establish views
normally. Bounded repair/backfill needs its own reviewed operation before editing older streams
without required views. [The findings](../../../docs/reports/e6-1-inline-decision-state.md)
record the new proofs and remaining extraction candidates.

## ES1 bounded accepted-batch append

Inventory also owns **stock issue**: positive quantities leave a selected stock position in
its base unit, and a complete requested batch must fit the loaded OnHand. IssueAsync
loads the required inline state at the observed header version before deciding eligibility.
InsufficientStock reports Available/Requested without staging any proposal. The issued v1
fact evolves historical state by subtraction; replay does not reapply current command rules.
This behavior does not reserve stock, update the availability catalog or coordinate a sale.

Inventory opening/receipts/issues and Purchasing drafts/lines now use the optional
package-free EventSourcing aggregate bookkeeping and configured native EF appender. Module
stores reuse existing loaders and prepare required views; library code owns GUIDs, positions
and one timestamp sampled from the configured TimeProvider. Business Contracts omit timestamps.
Domain Contracts, decisions, reducers, codec registration, required views/summary, ownership
admission, native save guards and final save/commit remain module/consumer code. Existing
native mappings/migrations, historical fixtures and explicit loading paths are preserved.
The independent EventStorageDemo counter uses that same append interface with captured-history
state, direct JSON and no required view or tenancy. T1's generated state-stored composition
does not acquire event sourcing. See [the reviewed ES1 scope](../../../docs/plans/es1-bounded-event-append.md).

## Current ES2 native EF model

The owner-approved replacement keeps one inline aggregate state per registered stream and
an independent explicit full-replay rebuilder. Purchasing summaries derive from that mapped state;
the dormant summary entity/table is removed by a new migration. Earlier E6 secondary-view
sections above record historical behavior, not another current projection/storage requirement.
Prior migrations and literal fixtures remain unchanged.

Each concrete command store takes its module's typed DbContext, event mapping and clock and
configures the shared aggregate-state mapping. It has no history reader or maintenance binding.
Separate StockPositionRebuilder/PurchaseOrderRebuilder implementations use the existing history
reader and effect-free reducer. Explicit role registrations retain the module context boundary.
Query services use InlineStateReader directly and native mapped-state filters/joins; no wrapper
projection class or additional stored summary is required. Changed results describe changes in
the unit of work; the caller's explicit native SaveChanges/commit establishes durability.

Appends and repair change the header ConcurrencyStamp. A stale writer/repair fails natively at
save and must roll back/dispose/reload. Repair preserves facts, event version and recorded times.
There is no pre-read admission gate, silent catch-up, scheduler or worker. A maintenance worker
remains planned; consumers can host reconciliation and own authorization, windows/locks, retries,
scheduling and scaling. [The current library contract](../../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md)
provides self-sufficient setup, errors and limits.

## Inventory durable incoming stock commands

Inventory owns IssueStockV1 and StockIssueDeclinedV1 alongside its existing recorded reply.
AddStockIssueInbox is separate opt-in after AddStockPositionCommands. The handler admits its
configured producer/Organization, establishes one fresh tenant context and invokes the existing
state-dependent command. Accepted event/state changes, outgoing reply and inbox completion share
the processor's native transaction. Handled business refusals complete with a decline reply;
technical/admission failures remain pending. Library inbox identity is delivery deduplication,
not Inventory authorization or stock eligibility.

Operational selection can span this module's tenant queue, but business access occurs only
after admission. No peer DbContext, shared cross-module transaction or automatic domain-event
publisher is introduced. [Executable adoption](../IntegrationTests/Scenarios/EventPersistence/README.md#durable-stock-issue-command-intake)
and [library setup](../../../src/Gloaming.Messaging/docs/inbox.md) describe the explicit lanes.
