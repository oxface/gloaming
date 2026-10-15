# Native CQRS consumer journey

Sales commands stage accepted domain decisions; Sales queries return current read
shapes from the same storage. Inventory availability is read through its typed
Contracts. Native scoped DI selects the concrete implementations. The composition
and policies belong to this sample; no CQRS library or registration runtime is added.

The executable reads on-hand 10 minus reserved 7, creates three Sales drafts,
changes one total from 40 to 45 and rejects a total of 105 against its limit of 100.
It searches the joined current draft/customer view with a minimum total of 30:
two matches, one result on the first page. The cheap draft is filtered before paging.
Reading availability does not reserve stock or establish that a draft can be fulfilled.

## Run and verify

Use .NET from the root `global.json`. The executable requires a fresh disposable
PostgreSQL database; it applies existing module migrations and finite setup rows.
It is intentionally not an idempotent production initializer. It uses fixture
identities rather than real authentication, tenant admission or authorization.

```bash
WHOLESALE_DEMO_CONNECTION_STRING='Host=localhost;Port=5432;Database=cqrs_demo;Username=postgres;Password=postgres' \
  dotnet run --project samples/Wholesale/CqrsDemo/CqrsDemo.csproj
dotnet test --project samples/Wholesale/CqrsDemo.Tests/CqrsDemo.Tests.csproj
```

Tests create disposable PostgreSQL 18.6 databases through Testcontainers, with the
resource reaper enabled. For the repository's rootless Podman setup:

```bash
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
  dotnet test --project samples/Wholesale/CqrsDemo.Tests/CqrsDemo.Tests.csproj
```

## Concrete consumer pattern

[`CqrsComposition`](CqrsComposition.cs) explicitly registers tenant/actor accessors,
native module DbContexts, `AddDraftOrderCommands`, `AddDraftOrderQueries`,
`AddInventoryQueries` and `AddStockPositionQueries`. Each operation gets its own
scope and immutable context. There is no handler discovery or untyped dispatch.

The command path in [`CqrsJourney`](CqrsJourney.cs) retains normal native control:

```csharp
await using var transaction = await sales.Database.BeginTransactionAsync(cancellationToken);
var result = await commands.ChangeLinesAsync(request, cancellationToken);
// Inspect Changed / Conflict / LimitExceeded, etc. Changed is still a proposal.
await sales.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The caller owns the scope, context, transaction, save, commit and disposal. Rejected
decisions leave no partial candidate. Storage/concurrency failures need rollback,
disposal and a fresh decision; no implicit retry is supplied. Domain rules, expected
versions and existing human/tenant checks remain in Sales. Submission and its local
required reactions still use the current Gloaming.EventReactions composition;
this journey exercises ordinary draft creation/editing rather than submission.

The read side uses separate typed operations:

```csharp
var available = await inventoryQueries.ReadAvailableAsync(3, cancellationToken);
var page = await draftQueries.SearchAsync(
    new SearchDraftOrders(CustomerNameContains: "Alpha", Currency: "EUR",
        MinimumTotal: 30, Offset: 0, Limit: 2), cancellationToken);
```

Sales' internal `DraftOrderReadView` is an unmapped query shape. Its native EF
query joins draft/customer on organization and customer ID, and derives line
count/total from Sales' relational children. `DraftOrderFilters` composes ordinary
`IQueryable` predicates over that joined shape; filtering precedes count, sorting
by total then ID, and offset/limit. Existing `ReadSummaryAsync` reuses the same view.
Neither rows nor `IQueryable` cross module Contracts.

All supplied filters combine with AND. Name matching is a literal case-sensitive
substring, including `%` and `_`; currency matches exactly. Inputs are not trimmed
or case-normalized. Missing filters impose no predicate. Negative minimum totals
or offsets, whitespace-only supplied strings and limits outside 1–100 are rejected.
An empty or beyond-end page retains the total count of filtered matches. Results
are untracked database values: pending tracked proposals are absent until saved,
and another connection sees them only after commit. Reads never save or repair data.

Count and page are two sequential statements over the same filtered query. Under
read committed, a concurrent commit can change membership between them. For a
common snapshot, the caller explicitly wraps the read in a native transaction:

```csharp
await using var snapshot = await sales.Database.BeginTransactionAsync(
    System.Data.IsolationLevel.RepeatableRead, cancellationToken);
var page = await draftQueries.SearchAsync(request, cancellationToken);
await snapshot.CommitAsync(cancellationToken);
```

Each read operation is sequential on its DbContext. Deterministic sorting does not
freeze later offset pages across independent operations. This is not cursor paging
or an export protocol. No cross-module snapshot/transaction is established by
separate Sales and Inventory reads, even though they share one physical database.

Inventory's existing `StockPositionFilters.WithAvailableAtLeast` filters JSONB
on-hand minus reserved before materialization and joins module-owned inline state
to its stream header on the complete tenant/stream key. It validates selected
inline metadata without replaying history. Its positive quantity guard and missing
legacy reserved-field fallback remain Inventory policy. Sales command reference
validation uses `IStockCatalog`; it never joins Inventory tables.

## Findings and deferred work

No new reusable mechanism was proven. Typed DI, EF query composition, native
transactions and module-owned result types suffice. The new filters remove no
cross-module technical duplication that warrants a library. Persisted projections,
generic repositories, mediator/pipeline abstractions and generic paging/filter
helpers remain unselected.

Template candidates are editable registrations, the operation scope, explicit
transaction ownership and native query/filter examples. T1's generated Catalog
composition is unchanged; richer CQRS generation, HTTP query binding/authorization,
query indexes/benchmarks, alternate sorting and provider semantics need separate
consumer requirements. An upper page-size limit does not bound offset scan cost
or aggregate-query cost.

The [scope](../../../docs/plans/c1-cqrs-consumer-patterns.md) records the pre-implementation
consumer proposal. The [slice report](../../../docs/reports/c1-cqrs-consumer-patterns.md)
separates new checks, historical evidence and remaining template work.
