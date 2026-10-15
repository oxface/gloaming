# C1: native CQRS consumer patterns

Scope presented before implementation, 2026-10-15. Base: fetched `origin/main`
`e400e73`; isolated branch `feat/cqrs-consumer-patterns`. This is consumer work,
not a proposal for a Gloaming library interface.

The owner approved the implemented outcome and source-based extraction assessment
on 2026-10-15. See the [report](../reports/c1-cqrs-consumer-patterns.md); no general
pipeline or new library was selected. Work remains uncommitted.

## Concrete journey and usage

Start with the actual `DraftOrderCommands`, `DraftOrderQueries` and
`StockPositionQueries` / `StockPositionFilters`. D2's current implementation is
Gloaming.EventReactions; its earlier marker/base proposals are superseded.

```csharp
services.AddSalesDatabase(connection);
services.AddDraftOrderCommands();
services.AddDraftOrderQueries();
services.AddInventoryDatabase(connection);
services.AddInventoryQueries(); // Sales checks SKU existence through IStockCatalog.
services.AddStockPositionQueries();

// Host establishes immutable admitted tenant/actor context in each operation scope.
var available = await inventoryQueries.ReadAvailableAsync(3, cancellationToken);

await using var transaction = await sales.Database.BeginTransactionAsync(cancellationToken);
var result = await commands.CreateAsync(request, cancellationToken);
// Changed contains a proposal; inspect the domain result before saving.
await sales.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);

var page = await queries.SearchAsync(
    new SearchDraftOrders(CustomerNameContains: "Alpha", Currency: "EUR",
        MinimumTotal: 30, Offset: 0, Limit: 2), cancellationToken);
```

One console executable will read available stock, create/edit a draft, reject an
over-limit decision and query the committed draft/customer summary. Inventory
availability is advisory; it neither reserves stock nor changes Sales policy.
Setup uses finite demonstration identities, native migrations and explicit saves.

## Exact bounded change

- Sales Contracts add `SearchDraftOrders`, `DraftOrderPage` and the typed
  `IDraftOrderQueries.SearchAsync` method. No `IQueryable` crosses Contracts.
- Internal `DraftOrderReadView` joins draft/customer on the complete ownership key
  and derives line count/total from Sales-owned children. `DraftOrderFilters`
  supplies composable native `IQueryable` predicates for customer name, currency
  and minimum total. Existing single-summary reads reuse that same view.
- Search applies every filter before count, deterministic total/ID sorting and
  offset/limit. Name matching uses literal, case-sensitive substring semantics;
  currency is exact. Reject whitespace-only supplied strings, negative minimums
  and offsets, and limits outside 1–100. Inputs are not silently normalized.
- Count and page execute sequentially with the same predicates on one context.
  They are two statements: callers needing a common snapshot explicitly open a
  native repeatable-read transaction. Default read committed can observe changes
  between statements; separate module reads have no shared snapshot guarantee.
- Inventory's existing JSONB availability filter/header join is adopted through
  Contracts without adding another search API or projection. Its positive
  quantity guard and reserved-quantity behavior remain module-owned.
- Add `samples/Wholesale/CqrsDemo` and `.Tests`, solution/Wholesale CI entries,
  consumer README, development commands and a dated slice report. No migrations,
  library changes, archive edits or generated template changes are planned.

## Ownership, failures and proofs

Native typed scoped DI selects the handlers and contexts. The host creates and
disposes scopes and owns transactions, saves, commits and cancellation. Commands
retain domain candidate validation, human/tenant admission and expected versions.
Storage/concurrency failures require rollback/disposal and a fresh decision;
there is no automatic retry. Queries do not track entities or save changes.
Sales reads Inventory only through Contracts; joins stay within module persistence.

PostgreSQL proofs will cover combined joined filters, independently expected
totals/counts, filtering before pagination, tied sort keys, empty/beyond-end pages,
current customer changes, tenant isolation, rejected/uncommitted/rolled-back
commands, stale versions, read snapshot composition, cancellation, input guards
and Inventory availability without event-history reads. Existing aggregate/local
reaction and architecture checks provide focused regression evidence, reported
separately from new tests and historical evidence.

Do not add a mediator, generic repository, pipeline, persisted projection engine
or generic paging/filter helper. The two consumers have different read shapes;
only demonstrated repeated technical complexity can justify extraction, after
owner review of any proposed new/changed library interface. Template work remains
a later selection of these exercised recipes. No commit or staging is authorized.
