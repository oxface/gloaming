# ES1 follow-up: registered inline state and appender simplification

Status: superseded recommendation, 2026-10-07. Owner feedback requires concentrating the
load/version/stage protocol behind a library-provided store. See the
[revised store direction](es1-library-write-store.md). The signatures and file map below
remain proposal history; the earlier review request does not authorize implementing them.
The owner authorized applying the settled directions. These exact new/changed public signatures still need the
review required by [AGENTS.md](../../AGENTS.md) and the
[extraction gate](library-extraction.md#extraction-and-strategy-gate) before implementation.
This revises the existing append capability; it does not start the deferred feature catalog.

## Inspected consumers and useful mechanism

Inventory and Purchasing already have concrete module-local stores that load validated inline
state, retain a native transaction observation, invoke domain operations and stage required
views alongside append. Their read side already uses native `{Aggregate}Queries` classes.
The independent counter uses a concrete store with captured history and direct JSON.
Reusing these paths avoids extracting a reader or adding a generic store facade with no
additional mechanism. Domain decisions, evolution and required-view definitions stay local.

The missing reusable obligation is enforcement at native saving: the public appender stages
only header/events, and an omitted main row can currently be saved through a module context.
A model-declared main-state relationship plus explicit save validation can reject that
omission, wrong ownership, wrong version and disconnected main-state changes. Module stores
retain their preparation logic; the validator does not run reducers or construct state.

## Proposed public surface

In `ModulithFoundry.EventSourcing.EntityFrameworkCore`, replace the current three-parameter
appender class with the following signatures. Existing adapter and prepared-handle types
remain; stored-row selection moves to preparation, so an instance can serve multiple stored
row types for its event/stream types. Commands continue to call their module store and see
none of these adapter or payload arguments.

```csharp
public sealed class EventAppender<TEvent, TStream>
    where TEvent : class
    where TStream : class, IEventStreamRecord
{
    public EventAppender(DbContext database, TimeProvider timeProvider);

    public PreparedEventAppend<TStream, TStoredEvent> Prepare<TStoredEvent>(
        TStream observedStream,
        IEventSourcedAggregate<TEvent> aggregate,
        EventRecordAdapter<TEvent, TStream, TStoredEvent> records,
        CancellationToken cancellationToken = default)
        where TStoredEvent : class, IStoredEventRecord;
}

public static class RequiredInlineStateExtensions
{
    public static ModelBuilder ConfigureRequiredInlineState<TStream, TStoredEvent, TState>(
        this ModelBuilder model,
        string streamType,
        Expression<Func<TState, long>> version,
        Expression<Func<TState, DateTimeOffset>> recordedAt)
        where TStream : class, IEventStreamRecord
        where TStoredEvent : class, IStoredEventRecord
        where TState : class;

    public static void ValidateEventStreamChanges(this DbContext database);
}
```

Native expressions select mapped properties during model configuration; they are not per-call
encoding/evolution delegates. The state type needs no library base or JSON interface. Require
ordinary independent single-table rows, an existing event-to-stream primary-key relationship,
and one state row per complete stream key using its existing FK/PK. State version must be an
unconverted, non-generated native concurrency token; recorded time a mapped DateTimeOffset.
Reject duplicate family registrations and unsupported mappings rather than infer a main view.

No new dependencies: the EF segment retains its aggregate-core project reference and native
EF Relational package. No Npgsql, tenancy, codec, DI, Marten or sample Contracts dependency is
added to the library. The modules select existing codec registries; raw counter encoding stays
independent. `IStoredEventRecord.Payload` remains JsonElement at the persistence boundary.

## Consumer usage and ownership

```csharp
// Inside the Inventory module's existing model configuration:
model.ConfigureRequiredInlineState<EventStream, StoredEvent, StockPositionCurrentRow>(
    StockPositionHistoryReader.StreamType, row => row.Version, row => row.RecordedAt);

// Inside BOTH existing SaveChanges override paths, before calling native base saving:
this.ValidateTenantChanges(() => RequiredOrganizationKey);
this.ValidateEventStreamChanges();

// The existing application journey still owns completion:
await using var transaction = await database.Database.BeginTransactionAsync(token);
var aggregate = await store.LoadForWritingAsync(id, expectedVersion, token);
if (aggregate is null)
    return;
// Domain operation checks the loaded state's actual eligibility.
if (!aggregate.TryIssue(quantities, out _))
    return;
store.StageAppend(aggregate, token);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

The module store calls `appender.Prepare(stream, aggregate, configuredRecords, token)` and
prepares every local participant before tracking. The appender continues to own GUIDs,
contiguous positions, one clock sample, payload cloning, mapped keys and single-use staging.
The save validator calls DetectChanges and examines the pending native tracker without I/O,
saving, committing, dispatching or rehydrating. Existing loads still validate observed state.
Consumer save overrides provide mandatory integration in the actual adopting contexts;
standalone adopters explicitly choose that integration. No optional interceptor is claimed
as unavoidable enforcement.

For registered families, require an active native context transaction and one matching pending
header, event batch and main row in the same save. Compare complete mapped ownership keys,
the header/state original version for updates, the advanced version and main recorded time.
Require exactly the contiguous new event positions through that version, chronological UTC
event times bounded by the old/new header times, and final event time equal to the new header
time. Historical creation may contain several recorded times; normal appender batches keep
their existing single-time guarantee. Creation covers positions 1 through the initial head.

Reject missing, detached, unchanged, deleted or inconsistent main state for an advancing
registered header. Reject main-state mutation without its matching append, header-only
advancement, registered deletion, and mutable/deleted facts in the configured event pair.
For event inserts in a configured pair, require the matching tracked advancing header so the
family can be classified without a database query. Explicit raw families may share that pair
and append without a main row; they still provide the header for their inserted events.
Unrelated state-stored writes remain unaffected.

The validator proves participant inclusion and metadata alignment, not semantic equivalence of
a JSON state to its historical facts. Consumer evolution/load tests prove that policy. It
protects the adopting context's tracked save paths; ExecuteUpdate/Delete, raw SQL, external
writers, removed save overrides and malicious model changes are outside this EF guarantee.
Database constraints/RLS are not added by this revision. Native concurrency and transaction
errors still come from EF/PostgreSQL. Failed saves require rollback and a fresh context.

Caller argument/configuration errors retain native ArgumentException variants. Invalid model
registration, absent transaction, omitted participants and tracker inconsistency raise
InvalidOperationException before SQL. Existing appender errors, cancellation and overflow
behavior remain. No new domain errors or conflict translation are introduced.

## Native query adoption and JSONB

Keep existing current/summary/history query Contracts. Add to Inventory's existing
`IStockPositionQueries` one `Task<IReadOnlyList<StockPositionHistory>> ReadAvailableAsync(
decimal requiredQuantity, CancellationToken cancellationToken)` operation. Require a positive
quantity, filter current inline rows with OnHand >= requiredQuantity, order by stream identity,
and materialize inside the module. Add internal `StockPositionFilters.WithOnHandAtLeast` over
IQueryable<StockPositionCurrentRow>. Validate each returned row against its joined stream
version/time through the existing projection checks. Demonstrate it after an accepted issue.

Use the existing inline-state JSON mapping for this one exercised native filter, with the
supported JSON property/numeric traversal; do not migrate state to typed JSON as part of this
revision. PostgreSQL tests must prove server filtering and tenant isolation, and observe that
the query does not read the event table. Event payload JSONB is selected by stream metadata
and decoded, never filtered by domain payload properties in ordinary application reads.

Add a focused independent-consumer PostgreSQL round-trip proof: prepare from a JsonDocument,
dispose its source before staging/saving, commit, load JsonElement in a fresh EF context, and
verify nested values, Unicode/escaped text and decimal precision semantically. Confirm the
native column is JSONB without relying on preserved textual property order. This proof uses
the existing reviewed appender API and can run before the interface revision is approved.

## Exact file and behavior map

All paths below are relative to the repository root. No existing source file is removed.

| File | Disposition and behavior |
| --- | --- |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventAppender.cs` | Replace class arity/constructor and move typed adapter validation to Prepare; preserve append mechanics. |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/RequiredInlineStateExtensions.cs` | New model registration and explicit save validator for required-state inclusion/metadata. |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md` | Revised public usage, supported integration and bypass boundaries. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/InlineViewMapping.cs` | Register the existing main-state FK/version/time without schema changes. |
| `samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/InlineViewMapping.cs` | Register main state; independent summary remains a required module participant. |
| `samples/Wholesale/modules/Inventory/Inventory/InventoryDbContext.cs` | Invoke validator in both native save override paths after tenant validation. |
| `samples/Wholesale/modules/Purchasing/Purchasing/PurchasingDbContext.cs` | Same save integration. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionStore.cs` | Use revised appender and private configured adapter; existing loading/required views stay local. |
| `samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderStore.cs` | Same appender adaptation; preserve independent summary evolution. |
| `samples/EventStorageDemo/CounterStore.cs` | Use revised appender; retain captured history, direct JSON and absence of inline state. |
| `samples/Wholesale/modules/Inventory/Inventory/InventoryHistorySeed.cs` | Prepare initial main state from decoded retained facts before tracking the finite imported history. |
| `samples/Wholesale/modules/Purchasing/Purchasing/PurchasingHistorySeed.cs` | Prepare initial main state and summary from retained facts before tracking the finite imported history. |
| `samples/Wholesale/EventPersistenceDemo/DemoJourneys.cs` | Explicit separate native transactions around finite module history seeds. Preserve authored history/output. |
| `samples/Wholesale/EventPersistenceDemo.Tests/HistoryReadTests.cs` | Adapt finite seed call sites to explicit module transactions; keep corruption/temporal fixtures and expectations. |
| `samples/Wholesale/modules/Inventory/Inventory.Contracts/IStockPositionQueries.cs` | Add the exact availability read operation described above. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionFilters.cs` | New internal reusable mapped-state filter. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionQueries.cs` | Implement availability read through native filtering/join/materialization. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionInlineProjection.cs` | Share existing metadata validation with selected query rows; reducer/staging ownership unchanged. |
| `samples/Wholesale/EventPersistenceDemo/StockIssueJourney.cs` | Exercise availability query before/after issue, preserving existing output lines. |
| `samples/Wholesale/EventPersistenceDemo.Tests/AppendTests.RequiredInlineState.cs` | New omission/tampering/save-path/rollback/concurrency and SQL filter consumer proofs. |
| `samples/EventStorageDemo.Tests/AppendTests.cs` | New JSONB lifetime/fresh-context proof; adapt appender call sites and prove instance reuse for distinct stored row types. |
| `docs/plans/es1-bounded-event-append.md` | Link this exact revision scope; retain previous reviewed interfaces as history. |
| `docs/plans/event-sourcing-capabilities.md`, `docs/plans/library-extraction.md`, `docs/design.md`, `docs/adr/0006-transactional-main-inline-state.md` | Update implementation status only after approval/verification. |
| `docs/reports/es1-bounded-event-append.md`, `docs/development.md`, `samples/Wholesale/EventPersistenceDemo/README.md`, `samples/EventStorageDemo/README.md` | Record executable adoption, checks and limits separately from previous evidence. |

Finite seed adaptation is necessary: those helpers currently persist registered-family events
without any main state and without explicit caller transactions. Preserve literal fixture bytes
and native migrations, but make their normal setup satisfy the same registered-family invariant.
Fault/corruption setup continues to use explicit privileged SQL as existing tests already do.

## Verification and review boundary

Use real PostgreSQL for omission/failure, concurrency, rollback, fresh-context recovery and
JSONB/native-query claims. Exercise sync and async saving, complete ownership keys, missing or
detached main state, wrong versions/times, unchanged-token bypass, missing event positions,
disconnected state changes, and mixed registered/raw families. Retain the existing rejected
batch, competing writers and fresh-context tests; prove appender-instance reuse through two
stored row types. Run both event suites, aggregate/codec/history/architecture checks, the
relevant existing HTTP suite, solution build, formatting/analyzers, pending-model checks,
archive verification and local documentation links.

This proposal supplies no new runtime proof by itself. Until exact interface review, only the
independent JSONB test and documentation are changed. Leave all revisions unstaged, preserve
the existing index, and obtain separate approval of the complete change set before committing.

The JSONB proof was implemented through the existing API and executed on 2026-10-07: all 47
independent-consumer PostgreSQL tests passed. This establishes the payload behavior described
above, not the proposed required-state enforcement or revised appender interface. See the
[dated ES1 report addition](../reports/es1-bounded-event-append.md#jsonb-follow-up-proof--2026-10-07).
