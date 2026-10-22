# ES1: library-provided aggregate write store

Historical design/execution record. The active interface is superseded by
[the owner-approved native EF replacement](es2-native-ef-simplification.md).
Renamed-source links lead to current replacements; removed mechanisms link to their replacement scope.

Historical ES1 checkpoint surface: secondary-view orchestration and its registration flag below
were superseded by the owner-approved [ES2 aggregate-only reduction](es2-aggregate-only-reduction.md).
Use the current library-local README for supported setup.

Status: implemented for owner review, 2026-10-07. The owner approved the IEventStore direction
and explicitly authorized starting changes to assess a concrete implementation. This replaces
the earlier narrow [appender proposal](es1-inline-state-enforcement.md). The owner also requested the default StoredEventRecord, clearer projection/store names and
library-local capability documentation. Those refinements are implemented; exact implementation
and public configuration types remain subject to line-by-line review. No staging or commit is
authorized; existing owner-staged entries are preserved. See [the slice report](../reports/es1-library-write-store.md)
for executions and remaining limits.

## Consumer journey and extraction finding

The previous PurchaseOrderStore and StockPositionStore repeated native transaction checks,
stream lookup/family validation, expected-version comparisons, association with a loaded
aggregate, and event/required-state coordination. Rearranging the appender's stored-row generic
did not remove that protocol. The new base store owns it; consumer bindings only select trusted
ownership, state encoding/reconstitution and secondary evolution. This is meaningful technical
extraction, rather than a facade over unchanged local orchestration.

Handlers inject IEventStore<TAggregate>. Creation and update eligibility belong to the domain:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
var aggregate = await orders.GetForWritingAsync(request.Id, request.ExpectedVersion, token);
aggregate ??= PurchaseOrderAggregate.Create(request); // Emits the opening fact immediately.
aggregate.SetLines(lines);
var staged = await orders.AppendAsync(aggregate, token);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

Update commands can return NotFound instead of creating. An omitted expectedVersion captures
and protects the fetched version; a supplied expectation also rejects a stale command. New
streams are observed at zero. Reconstitution returns the observed version and no pending
facts, without running today's command eligibility. Empty decisions skip AppendAsync. The
aggregate keeps accepted facts in order; the store does not clear or rebase them after save.

The aggregate may encapsulate its reducer or call a pure module-local Evolve function.
ApplyChanges changes accepted candidate/pending bookkeeping after validating the complete
batch. Evolve computes state from facts and can be reused by historical reads. Main state
encoding stores the already evolved candidate; secondary projections evolve their own
committed state. No second application to the advanced main candidate occurs.

## Exact public surface and configuration

All new persistence types live in ModulithFoundry.EventSourcing.EntityFrameworkCore:

```csharp
public interface IEventStore<TAggregate> where TAggregate : class
{
    Task<TAggregate?> GetForWritingAsync(Guid id, long? expectedVersion = null,
        CancellationToken cancellationToken = default);
    Task<EventAppendResult> AppendAsync(TAggregate aggregate,
        CancellationToken cancellationToken = default);
}
public sealed record EventAppendResult(long Version, DateTimeOffset RecordedAt);

public sealed class StoredEventRecord : IStoredEventRecord
{
    public Guid EventId { get; set; }
    public Guid StreamId { get; set; }
    public long StreamVersion { get; set; }
    public string EventName { get; set; }
    public int SchemaVersion { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public JsonElement Payload { get; set; }
}

public abstract class EventStore<TAggregate, TEvent, TStream, TStoredEvent>
    : IEventStore<TAggregate>
    where TAggregate : class, IEventSourcedAggregate<TEvent>
    where TEvent : class
    where TStream : class, IEventStreamRecord
    where TStoredEvent : class, IStoredEventRecord
{
    protected EventStore(DbContext database,
        EventRecordAdapter<TEvent, TStream, TStoredEvent> records, TimeProvider timeProvider);
    protected abstract TStream CreateStream(Guid id);
    protected virtual Task<TAggregate> LoadAggregateAsync(TStream stream, CancellationToken token);
    protected void ConfigureMainState<TRow>(InlineAggregateAdapter<TAggregate, TRow> adapter)
        where TRow : class, IInlineStateRecord;
    protected void ConfigureRequiredProjection<TRow>(InlineEventProjection<TEvent, TRow> projection)
        where TRow : class, IInlineStateRecord;
    // Implements both IEventStore operations above.
}

public interface IInlineStateRecord
{
    Guid StreamId { get; set; }
    long Version { get; set; }
    DateTimeOffset RecordedAt { get; set; }
}
public abstract class InlineAggregateAdapter<TAggregate, TRow>
    where TAggregate : class where TRow : class, IInlineStateRecord
{
    public abstract TAggregate Restore(TRow state);
    public abstract TRow CreateRecord(TAggregate aggregate);
}
public abstract class InlineEventProjection<TEvent, TRow>
    where TEvent : class where TRow : class, IInlineStateRecord
{
    public abstract TRow Evolve(TRow? committed, IReadOnlyList<TEvent> events);
}
public sealed class InlineProjectionStorage<TStream, TRow>
    where TStream : class, IEventStreamRecord where TRow : class, IInlineStateRecord
{
    public InlineProjectionStorage(DbContext database);
    public Task<TRow> LoadAsync(TStream stream, CancellationToken token = default);
    public void Validate(TStream stream, TRow row);
}
```

RequiredInlineStateExtensions exposes these two native integration calls:

```csharp
public static ModelBuilder ConfigureRequiredInlineState<TStream, TStoredEvent, TState>(
    this ModelBuilder model, string streamType, bool isMainState = true)
    where TStream : class, IEventStreamRecord
    where TStoredEvent : class, IStoredEventRecord
    where TState : class, IInlineStateRecord;
public static void ValidateEventStreamChanges(this DbContext database);
```

Register the mapped main row first, then any required secondary row with isMainState:false.
Each registered family has one main row type; row types are distinct between families and
participants. Rows have ordinary complete-stream-key primary/foreign keys, native long version
concurrency tokens and UTC recorded-time metadata. Existing mappings, provider JSONB declaration
and global filters remain native. Configuration snapshots are native EF model annotations;
no discovery or generated code. The consumer explicitly calls ValidateEventStreamChanges
from both synchronous and asynchronous native save overrides.

CreateStream supplies trusted ownership/extra fields once. The base assigns stream identity,
family and append metadata. Adapters return fresh detached candidates containing state fields;
the library copies complete foreign-key values from the trusted observed header and assigns
version/time. Restore owns decoding and aggregate construction. Required secondary reducers
return fresh state without mutating the committed input. Raw streams override LoadAggregateAsync
to reuse their existing captured-history reader; inline consumers do not reimplement loading.

The default StoredEventRecord is optional for ordinary non-tenant envelopes. It has no
encoding or provider policy and can hold different payload shapes selected by durable event
name/schema. Custom envelopes remain available for complete ownership keys/extra fields.

The four implementation generics describe domain facts and native mappings once. TStoredEvent
is an envelope, not a single event type or a projection. A purchase-order stream already stores
Drafted and LineSet through one envelope while maintaining main and summary rows. The handler
sees neither that generic nor JsonElement. EventRecordAdapter and the lower-level appender
remain available without changing their previously reviewed interface.

## Ownership, dependencies and guarantees

Dependencies remain the existing package-free aggregate core and native EF Relational.
Npgsql, tenancy, codec, provider/context/migration setup and module Contracts remain selected
consumer dependencies. No Marten runtime package, DI convention, transaction wrapper,
SaveChanges interceptor or new DbContext base is introduced.

Library mechanisms:

- Observe one scoped header and capture its native version; validate configured family/UTC
  metadata and optional command expectation. Load required state against the complete key,
  verify version/time, and validate effect-free aggregate reconstitution.
- Bind append to the observed aggregate/store/context/native transaction. A refetch supersedes
  the old untracked root. One terminal append per stream/context; accepted facts must be nonempty.
- Reuse the appender's GUID generation, ordered positions, single UTC clock sample, complete-key
  validation, encoded payload cloning and native header staging. Prepare every required state
  candidate before tracking any proposal, including checking existing tracked inline keys.
- Stage main state and configured required secondary state with the same version/time, preserving
  observed original version tokens. Native EF concurrency and PostgreSQL uniqueness arbitrate
  competing append/creation. Stage is not a durability result.
- At configured native saves, require an active explicit transaction, the advancing header,
  every declared changed inline row and the entire contiguous inserted event range. Reject
  omitted/changed participant metadata, independent state edits and event mutation/deletion.

Consumer policy: eligibility, candidate invariants, reducers, typed JSON options/registrations,
required-view selection, tenant admission, trusted new-stream ownership, Contracts, final save,
commit/rollback, known-constraint conflict classification and fresh-context retry decisions.
The guard validates metadata/participation, not semantic equivalence of a manually altered
state body. Configure guards on every participating context's save paths; annotations alone
do not execute them. Arbitrary SQL, bulk ExecuteUpdate/Delete, external writers and overridden
save bypasses are outside this tracked-write contract. No database-level RLS claim is added.

After rollback/failure discard context and proposal, reload and decide again. Do not replace
a transaction and save retained proposals. Tracking failures have no in-memory rollback
boundary. Concurrent context use, manual tracker surgery and mutable/side-effecting adapters
are unsupported. Missing/behind state fails; ahead state is a concurrency outcome, never
implicit repair. No snapshot-tail reader, global order or multi-stream aggregate is promised.

| Outcome | Error/behavior |
| --- | --- |
| Missing stream | Null, retained version-zero observation; no tracking until creation append. |
| Explicit expectation mismatch / ahead inline row | DbUpdateConcurrencyException. |
| Invalid family/header, missing/behind state, bad reconstitution | InvalidDataException. |
| Invalid ID/negative expectation | Standard argument error. |
| No active transaction, superseded root, repeated/empty append, tracked key, invalid registration/save participants | InvalidOperationException. |
| Encoding/evolution, cancellation, overflow, native save failures | Original exception; no automatic retries or error translation. |

## Exact file and behavior change map

Paths are relative to the repository root. Existing files remain in place; no demo, migration,
fixture or archived file is deleted. This maps the store revision beyond the prior ES1 surface.

| Files | Behavior |
| --- | --- |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/IEventStore.cs` (new) | Aggregate-only write interface and staged metadata. |
| Same directory: `EventStore.cs` (new) | Native lookup, version/observation protocol and configured append/state coordination. |
| Same directory: `InlineAggregateAdapter.cs` (new) | State encoding/reconstitution, secondary evolution and row metadata contracts. |
| Same directory: `InlineProjectionStorage.cs` (renamed from `InlineState.cs`) | Complete-key native loading, metadata checks and internal state preparation/staging. |
| Same directory: `StoredEventRecord.cs` (new) | Default optional envelope, with direct provided-store PostgreSQL adoption proof. |
| Same directory: `RequiredInlineStateExtensions.cs` (new) | Explicit model declarations and tracked native-save validation. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionStore.cs`; `samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderStore.cs` | Replace duplicated protocol with base-store configuration; retain native ownership and reducers. |
| Same module directories: `StockPositionAggregate.cs`, `PurchaseOrderAggregate.cs` | Real Create factories emit opening events; effect-free FromState retained. |
| Same directories: `StockPositionCommands.cs`, `PurchaseOrderCommands.cs`; module roots `InventoryRegistration.cs`, `PurchasingRegistration.cs` | Inject/register IEventStore; GetForWriting/Create/Append; preserve business outcomes. |
| Same directories: `StockPositionCurrentRow.cs`, `PurchaseOrderCurrentRow.cs`, `PurchaseOrderSummaryRow.cs` | Implement technical row metadata; fresh candidate encoding/evolution; retain fixture metadata helpers. |
| Same directories: both `InlineViewMapping.cs`; module roots `InventoryDbContext.cs`, `PurchasingDbContext.cs` | Declare required main/secondary participation and validate both native save paths. No schema changes. |
| Same directories: `StockPositionInlineProjection.cs`, `PurchaseOrderInlineProjection.cs` | Reuse native required-state loading; remove unused local staging bodies now owned by the base. |
| Inventory directory: `StockPositionQueries.cs`, `StockPositionFilters.cs` (new); `Inventory.Contracts/IStockPositionQueries.cs` | Native mapped-state availability filter and scoped read operation; no event payload predicates. |
| Module roots: `InventoryHistorySeed.cs`, `PurchasingHistorySeed.cs`; `samples/Wholesale/EventPersistenceDemo/DemoJourneys.cs`; `samples/Wholesale/EventPersistenceDemo.Tests/HistoryReadTests.cs` | Preserve literal finite histories; seed matching main/summary in explicit native transactions under the stronger contract. |
| `samples/Wholesale/EventPersistenceDemo/StockIssueJourney.cs` | Exercise availability filter after the real issue decision; preserve established output. |
| `src/Gloaming.EventSourcing/samples/EventStorageDemo/CounterStore.cs`, `CounterAggregate.cs`, `CounterCommands.cs` | Adopt shared raw write protocol, retain captured-history/direct JSON reader, no inline row; Create emits Started. |
| `src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/AppendTests.cs`, `AppendTests.DefaultEnvelope.cs` (new); `samples/Wholesale/EventPersistenceDemo.Tests/AppendTests.RequiredInlineState.cs` (new) | Store observations/reconstitution, JSONB lifetime/round trip, required-save faults/recovery and translated queries on PostgreSQL. Retain competing-writer/rollback suites. |
| Library/sample READMEs; `docs/design.md`, `docs/development.md`, both ES1 plans/reports, `docs/plans/library-extraction.md`, `docs/plans/event-sourcing-capabilities.md`, `docs/adr/0006-transactional-main-inline-state.md` | Actual API/setup, ownership, supported lifecycle, deferred features and new-versus-historical evidence. |

The latest refinement also renames internal coordination to aggregateState, requiredProjections,
ProjectionBinding and loadedStreams/LoadedStream; splits save validation into named checks;
and moves the capability catalog to the EF package's docs/capabilities.md with a root pointer.
The core receives its own docs/capabilities.md; all other package READMEs retain local supported
contracts and now record deferred context. See [the refinement report](../reports/es1-envelope-and-library-docs.md).
Library documentation ownership is recorded in the repository convention. Package-family
folders/tests and a possible EventSourcing.Postgres adapter are recorded proposals only.

No dependency/schema migration, template preset, archive modification or checkpoint reset.
Architecture follow-up edits from earlier turns remain preserved. Exact complete commit approval
is still required, including the existing index and all subsequent unstaged changes.

## Reference and evidence boundary

The archived [CorrectStockQuantityHandler](../../archive/proof-sample/modules/Inventory/Inventory/StockPositions/CorrectStockQuantity/CorrectStockQuantityHandler.cs)
and [StockPositionStore](../../archive/proof-sample/modules/Inventory/Inventory/StockPositions/Persistence/StockPositionStore.cs)
show the desired workflow but keep technical orchestration local. Its cooperative repair gate
is not extracted. The archived [PurchaseOrderStore](../../archive/proof-sample/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderStore.cs)
likewise coordinates native streams and explicit projections. These are inspected source,
not fresh runtime proofs or implementation authorization.

Marten's [command workflow](https://martendb.io/scenarios/command_handler_workflow.html#fetchforwriting)
captures a single-stream version for concurrency and selects aggregate loading by projection
lifecycle. Its [append API](https://martendb.io/events/appending.html) permits multiple CLR fact
types in one stream. Our implementation uses explicit native EF mappings and caller-owned
saving rather than generated session persistence. Marten's broader loading/async guarantees
are not inherited.

Keep JsonElement/JSONB at the heterogeneous persistence boundary and reuse JsonEventCodec
where selected. The counter proves independent direct JSON adoption. Defer repair, async,
snapshots, messaging, audit, cross-module transactions and template events; see the
[capability catalog](event-sourcing-capabilities.md) for their separate proofs. Stop after this
single store/append capability is reviewable.
