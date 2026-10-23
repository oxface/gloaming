> This is retained integration-test scenario documentation. The former standalone product
> executable was retired by S1. Use the [Wholesale product](../../../README.md) and its
> canonical API/worker/maintenance roles; this directory owns fixture/journey support only.
> Run proofs with `dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj`.

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

For separately provisioned restricted-role RLS, use the explicit
[`--tenant-isolation` mode](TenantIsolation/README.md). That mode covers Sales reads and
proofs of existing-draft edits; the ordinary journey below retains its original composition.

Use .NET from the root `global.json`. The executable requires a fresh disposable
PostgreSQL database; it applies existing module migrations and finite setup rows.
It is intentionally not an idempotent production initializer. It uses fixture
identities rather than real authentication or tenant admission. Sales now enforces
resource grants: the creating human receives Edit, which includes Read, so the finite
journey retains its output. A caller with another identity receives no implicit access.

```bash
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

Tests create disposable PostgreSQL 18.6 databases through Testcontainers, with the
resource reaper enabled. For the repository's rootless Podman setup:

```bash
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
  dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
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
Neither rows nor `IQueryable` cross module Contracts. Sales' correlated grant EXISTS
predicate runs before detail loading, totals, count and paging. Read/Edit overlap cannot
duplicate draft results. Read-only subjects cannot stage edits or submission; the same
checks run on direct Contract calls. See [Sales permissions](../../../modules/Sales/README.md).

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

The [A2.1 permission slice](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/a2-1-sales-resource-permissions.md) adds
direct human/system grants and native HTTP adoption; its query tests cover sparse grants,
revocation, tenant-colliding IDs and count/page grant-snapshot changes. This supplies no
grant editor, role/group engine, export/AI endpoint or strict permission-at-commit fence.
RLS is exercised separately by the opt-in mode linked above, with its own credentials,
table inventory, native transaction setup and limitations.

Registrations, operation scopes, explicit transaction ownership and native queries remain
consumer-owned examples. The template generator is retired. Additional query/authorization
compositions, indexes/benchmarks, alternate sorting and provider semantics need concrete
consumer requirements. An upper page-size limit does not bound offset scan or aggregate-query cost.

The [scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/c1-cqrs-consumer-patterns.md) records the pre-implementation
consumer proposal. The [slice report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/c1-cqrs-consumer-patterns.md)
records the original scope, dated checks and limitations; the
[active audit](../../../../../docs/reports/gloaming-library-audit.md) owns current cleanup.
