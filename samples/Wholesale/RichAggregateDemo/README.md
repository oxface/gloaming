# Rich state-stored aggregate with relational children

A Sales draft owns a collection of line entities stored in `sales.draft_orders` and
`sales.draft_order_lines`. It is a state-stored write model: there is no event stream,
replay, domain-event collection or dispatcher in this capability. The existing Sales/Inventory
assemblies still have their other optional technical dependencies; this is not proof that those
assemblies are dependency-free.

## Run and consumer setup

Requires the pinned .NET 10 SDK and a **fresh disposable PostgreSQL database**:

```sh
WHOLESALE_DEMO_CONNECTION_STRING='Host=localhost;Database=rich_draft_demo;Username=postgres;Password=local' \
  dotnet run --project samples/Wholesale/RichAggregateDemo/RichAggregateDemo.csproj
dotnet test --project samples/Wholesale/RichAggregateDemo.Tests/RichAggregateDemo.Tests.csproj
```

The executable applies existing module migrations plus Sales's forward draft migration,
seeds one customer/catalog entry, creates a draft and changes its lines. Fixed demonstration
identities make this finite setup unsuitable for rerunning against an existing database.
Tests use Testcontainers PostgreSQL 18.6; rootless Podman uses the socket environment in
[development commands](../../../docs/development.md).

Expected output:

```text
Draft v2: 2 relational lines, total=45.00 EUR; limit=100.00 EUR.
Added, changed and removed children; rejected total=105.00 EUR without changing state.
Joined current customer name without changing the draft or reserving catalog stock.
```

The host explicitly configures SalesDbContext, trusted actor/tenant scopes, an IStockCatalog
implementation, AddDraftOrderCommands() and AddDraftOrderQueries().
[DraftOrderComposition](DraftOrderComposition.cs) shows the finite native composition. Sales
uses Inventory.Contracts only; its commands do not depend on Inventory's implementation.

The new [business Contracts](../modules/Sales/Sales.Contracts/IDraftOrderCommands.cs) stage one
complete candidate in an already active native Sales transaction. Changed is a proposal, not
committed success:

```csharp
await using var transaction = await sales.Database.BeginTransactionAsync(token);
var result = await commands.ChangeLinesAsync(
    new ChangeDraftOrderLines(orderId, expectedVersion,
        [new DraftOrderLineChange.Change(lineId, 3, 10)]), token);
if (result is DraftOrderChangeResult.Changed)
{
    await sales.SaveChangesAsync(token);
    await transaction.CommitAsync(token);
}
```

The caller handles preflight Conflict/NotFound, missing references and LimitExceeded, plus
native save/commit faults. DraftOrderFailures.IsVersionConflict recognizes this schema's root/
child concurrency and duplicate identity failures only; duplicate business codes and unrelated
constraints are not reclassified. Roll back and dispose the whole operation after failure.
One staged mutation per order is supported between saves; a second unsaved proposal rejects.
Saving successfully and accepting tracked changes permits a later mutation in the same native
transaction, but neither save independently establishes durability. False acceptAllChangesOnSuccess
requires deliberate AcceptAllChanges before continued tracked mutations.
A later transaction requires a fresh operation scope; do not reuse a cached tracked graph as
a current read across transaction boundaries.

## Ownership and domain rules

The draft root owns its line identities and child lifecycle. Each line has a catalog SKU,
positive quantity and nonnegative captured unit price. Line identities are local to an order;
two drafts may use the same child ID. Existing identities cannot be removed and re-added in
the same batch to switch their SKU: replace with a new child identity. Duplicate SKUs are allowed
as separate lines with potentially different captured terms.

The draft stores only a CustomerId reference to Sales's separate customer profile aggregate.
A same-Organization composite FK enforces that reference. It does not store a customer object
or mutate the profile. Catalog SKUs are checked through a public read contract for creation
and newly added lines; there is no cross-module database FK. Stock is not reserved, and a catalog
read does not prove eligibility at Sales commit or provide authoritative pricing.

A trusted human caller supplies a positive, immutable total limit and a three-uppercase-letter
currency label. This bounded label validation is not an ISO currency catalogue. The limit is
sample policy, not a credit/approval engine; real applications must authorize prices/limits.
All unit prices use the draft currency. The final graph must contain at least one line, have a
representable total and fit its limit. A mutation batch can temporarily exceed that limit or
remove all children while constructing its final candidate. A rejected final candidate leaves
the observed domain state and native tracked values unchanged.

An immutable domain graph is adapted to separate native EF rows. This keeps the domain's Money
value and constructors independent of ORM materialization while exposing ownership, concurrency
and delete staging explicitly. Directly mapping a domain graph is another valid consumer choice;
this example does not mandate a row-adapter layer or generic repository.

## Persistence and read boundaries

- Root and children are loaded together with an explicit single-query Include. Ordinary writes
  require a complete graph; partial child loading is insufficient to validate root-wide rules.
- Each accepted batch advances the root version once. Native UPDATE predicates fence competing
  edits even when they touch different children. Children have no independent aggregate version.
- Both SaveChanges override families run the owning sample guard: tracked child mutations require
  a participating root, an advanced version and a complete valid final graph. Customer, code,
  currency and limit are fixed after draft creation.
- Removed children are explicitly removed from the navigation and marked Deleted; FK delete behavior
  is Restrict. Root deletion, orphan inference and cross-order child transfer are not supported.
- Reads return committed state using no-tracking queries. The summary joins the draft to the
  current customer name and aggregates its lines in native SQL; no stored projection is maintained.

ExecuteUpdate/Delete, raw SQL and direct provider access bypass the tracked-save guard. The
proof suite executes a bulk-write counterexample that breaks the root invariant without advancing
its version. These are outside the supported business-write protocol, rather than protected by
the SaveChanges override. Ordinary schema/tenant constraints remain database-enforced; the root-wide
total rule is not a universal database constraint. Corrupt native state is rejected on domain load,
not silently repaired. Infrastructure lifecycle SQL remains governed by its own existing protocols.

## Proofs and extraction decision

The proof suite checks pure complete-candidate decisions and real PostgreSQL round trips,
child add/change/remove identity, rejected batches, root concurrency across different children,
required-write/commit rollback, fresh-scope recovery, save overload guards, tenant-bearing FKs,
native write bypass limits and migration over populated prior Sales storage. The executable
journey is invoked directly by tests; no process-death/deployment claim follows.

**No new reusable mechanism was proven.** Domain rules, Money/line values, row adaptation and the
small complete-graph/version guard remain consumer-owned. The guard is a possible comparison point
for later repeated obligations, not a library or supported arbitrary-graph mapper. T1 templates,
domain-event handling, D2 SaveChanges dispatch and durable reactions remain separate increments.
[The D1.1 report](../../../docs/reports/d1-1-rich-state-stored-aggregate.md) records fresh evidence,
review-worthy files and remaining gaps.
