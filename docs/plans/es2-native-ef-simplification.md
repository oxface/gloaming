# ES2 reconsideration: direct EF writes and independent rebuilding

Status: owner-approved replacement, 2026-10-07 ("agree, proceed"). Implementation and
verification complete; [495 active tests plus 10 external T1 tests passed](../reports/es2-single-stream-rebuilding.md#native-ef-replacement-2026-10-07).
New changes stay unstaged; commit approval remains separate.
The proposal below records the exact reviewed interface and behavior scope.

## Naming follow-up under owner review

The requested readability cleanup uses TStreamRecord, TStoredEventRecord and
TInlineStateRecord to distinguish persistence records from domain events/aggregates. Internal
names use observedStream/streamRecord and streamType rather than header/family. There is no
generic-arity or behavior change. The reader retains its current DbContext constructor and
two generic arguments; a typed-context variant is not introduced.

Proposed public rename, not implemented: IEventStreamRecord.ConcurrencyStamp to
ConcurrencyToken. Preserve the mapped `concurrency_stamp` column, GUID generation and native
concurrency behavior. Concrete scope would include the interface, storage configuration,
store/rebuilder/save validation, consumer stream records and affected tests, current model
snapshots, and current library documentation. Historical migrations and recorded design/proof
evidence stay intact. This property identifies a technical token; Version remains a native
concurrency token as well. No new locking or retry mechanism is proposed.

## Finding and recommendation

The aggregate-only reduction kept the earlier protocol and mostly moved its implementation.
It did not remove the main coupling: registering inline state makes ordinary writes require
maintenance configuration, a replay reader, a provider gate and context-wide admission state.
Prepared replacement objects and a public two-step append then carry defensive lifecycle checks
around that protocol. Keeping those shapes is not justified merely because tests exist.

Recommend native optimistic concurrency with a separate technical header stamp. Keep the
small command store, make rebuilding a genuinely independent implementation, and remove the
shared/exclusive gate and its package. Accept native conflicts instead of blocking writers
before they load. This retains safe online persistence but changes scheduling behavior: a
writer can decide from pre-repair state, then lose at save; it must reload and decide again.
The current gate instead delays loading. Neither algorithm can undo a bad command committed
before maintenance, and neither fences arbitrary writers that ignore its native contract.

EF already supports application-managed GUID concurrency tokens and original-value predicates:
[official concurrency documentation](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
Explicit native transactions retain all-or-nothing completion:
[official transaction documentation](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
The suitability of a stamp on this particular stream header is our design inference and the
limited SQL experiment below, not a supported EF-library guarantee yet.

An offline-only rebuild with all writers paused and drained could remove the gate without a
stamp. It would be a smaller but weaker contract, requiring operational exclusion. Prefer the
stamp: online repair is an already demonstrated obligation, and one native concurrency field
can replace the much larger cooperative protocol. No new lock manager or provider abstraction.

## Meaning of versions and natural evolution

The stream's Version remains its event count/head. Aggregate ExpectedVersion and Version keep
their existing domain-facing meanings. Add Guid ConcurrencyStamp to the stream header, map it
as an EF concurrency token, and generate a new value on every library append or rebuild.
Repair changes this technical stamp while preserving event version and recorded timestamps.
Raw history-backed stores use the same header mapping and append stamping, without required state.
It is not a command argument, event identifier, ordering value or projection progress counter.

The original header stamp participates in native UPDATE predicates. If repair commits after
writer loading, that writer's header update matches no row. If append commits after repair
capture, repair's header update matches no row. The consumer rolls back/disposes the entire
transaction/context. State and event SQL already issued in that transaction cannot commit on
a failed header predicate. Missing state is inserted through native EF/PK constraints. Retain
state Version as its concurrency token; no extra stamp is needed on the state row.

Rebuilding already applies retained events through the consumer's historical reducer.
PreparedAggregateRebuild does not evolve the aggregate: it attaches/replaces its EF state row
and records what later save validation may accept. Calling the aggregate's command ApplyChanges
for historical replay would collect pending facts and re-run current candidate validation.
Use the same pure evolution via effect-free reconstruction instead. Natural replay and a
small EF replacement operation are sufficient; no separate prepared replacement class is needed.

## Proposed public surface

Keep IEventStore<TAggregate>, its two methods, EventAppendResult, IAggregateRebuilder<TAggregate>,
AggregateRebuildResult, IInlineStateRecord, durable heterogeneous envelopes and event codecs.
Rename EventRecordAdapter<TEvent,TStream,TStoredEvent> to EventRecordMapping with ToRow(event,
capturedHeader); its StreamType property and selected envelope generics remain unchanged.
Use Mapping for encoding/decoding conversions and Reader for lookup/validation.
Keep the package-free aggregate contract/base; do not introduce a persistence-aware aggregate base.

Changes to the EF surface:

```csharp
public interface IEventStreamRecord
{
    Guid Id { get; set; }
    string StreamType { get; set; }
    long Version { get; set; }
    Guid ConcurrencyStamp { get; set; } // new technical native concurrency token
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}

public abstract class EventRecordMapping<TEvent, TStream, TStoredEvent>
    where TEvent : class
    where TStream : class, IEventStreamRecord
    where TStoredEvent : class, IStoredEventRecord
{
    public abstract string StreamType { get; }
    public abstract TStoredEvent ToRow(TEvent eventData, TStream capturedHeader);
}

public abstract class AggregateStateMapping<TAggregate, TRow>
    where TAggregate : class
    where TRow : class, IInlineStateRecord
{
    public abstract TAggregate ToAggregate(TRow stateRow);
    public abstract TRow ToRow(TAggregate aggregate);
}

public sealed class InlineStateReader<TStream, TRow>
    where TStream : class, IEventStreamRecord
    where TRow : class, IInlineStateRecord
{
    public InlineStateReader(DbContext database);
    public Task<TRow> ReadAsync(TStream capturedHeader,
        CancellationToken cancellationToken = default);
    public void Validate(TStream capturedHeader, TRow stateRow);
}

public abstract class AggregateRebuilder<TAggregate, TEvent, TStream, TRow>
    : IAggregateRebuilder<TAggregate>
    where TAggregate : class, IEventSourcedAggregate<TEvent>
    where TEvent : class
    where TStream : class, IEventStreamRecord
    where TRow : class, IInlineStateRecord
{
    protected AggregateRebuilder(DbContext database, string streamType,
        AggregateStateMapping<TAggregate, TRow> stateMapping);
    protected abstract Task<IReadOnlyList<ReplayedEvent<TEvent>>> ReadEventsAsync(
        TStream capturedHeader, CancellationToken cancellationToken);
    protected abstract TAggregate Rehydrate(TStream capturedHeader,
        IReadOnlyList<TEvent> events);
    public Task<AggregateRebuildResult?> RebuildAsync(Guid id,
        CancellationToken cancellationToken = default);
}
```

These are the reviewed interface declarations; bodies are omitted. The implementation uses
eventData rather than the reserved parameter name event to satisfy enforced analyzer CA1716. InlineStateReader replaces
InlineProjectionStorage and performs only native filtered lookup and metadata validation.
Its foreign-key metadata maps state-row properties to the header's primary key, often also
the state's primary key. Name it streamForeignKey, not reference or a generic key. Key metadata
and captured key values must have different names.

AggregateStateMapping replaces InlineAggregateAdapter with explicit conversion names. ToRow
encodes a fresh detached row; the library fills keys, version and time. This is a mapping,
not an additional projection, generic projector engine or maintenance state machine.
The independent rebuilder binds the existing reader/reducer through its two protected methods;
remove the extra EventStreamReplay adapter class. ReplayedEvent metadata remains necessary.

EventStore<TAggregate,TEvent,TStream,TStoredEvent> remains write-only. Replace ConfigureMainState
with the following protected configuration method:

```csharp
protected void ConfigureInlineState<TRow>(
    AggregateStateMapping<TAggregate, TRow> mapping)
    where TRow : class, IInlineStateRecord;
```

The base constructor keeps its DbContext/clock parameters and uses the renamed
EventRecordMapping<TEvent,TStream,TStoredEvent>. Remove IAggregateRebuilder implementation, RebuildAsync and
ConfigureRebuilding from that base. The internal state binding has no repair methods. Registered
write loading requires the state mapping and explicit transaction, never replay/provider setup.
The raw counter still overrides its existing LoadAggregateAsync path and omits inline registration.

Remove the public EventAppender and PreparedEventAppend two-step interface. Its useful encoding
and metadata work becomes internal batch encoding called by Store.AppendAsync. A plain internal
encoded batch may carry rows/version/time; it has no Stage method or deferred operation lifecycle.
No real executable consumer currently uses the lower-level interface: its external callers are
proof tests. The independent raw counter already uses the store. Replace those tests through
that supported store interface rather than retain a public interface solely for tests.

ConfigureRequiredInlineState and ValidateEventStreamChanges retain their public signatures.
Remove ConfigureInlineProjectionRebuilding, EventStreamWriteGate and EventStreamAccess.
Remove the optional Postgres admission package. EF Relational remains the native dependency;
PostgreSQL remains the proven runtime, not a provider-neutral certification. A later actual
pessimistic-locking need can justify a new provider package then.

Register write and maintenance roles as different scoped implementations using explicit typed
module contexts. AddEventStore<TAggregate,TStore> registers only IEventStore; its constraint
no longer requires IAggregateRebuilder. Add an overload AddEventStore<TAggregate,TStore,TRebuilder>
for full aggregate composition and AddAggregateRebuilder<TAggregate,TRebuilder> for maintenance-only
composition. Each uses ordinary scoped/TryAdd registration and the existing optional clock default.
No cross-role shared-instance requirement or custom DI conflict/preflight framework. Keep
same-role concrete/interface identity if both are registered; keep typed context isolation.
Reviewed registration signatures:

```csharp
public static IServiceCollection AddEventStore<TAggregate, TStore>(
    this IServiceCollection services)
    where TAggregate : class
    where TStore : class, IEventStore<TAggregate>;
public static IServiceCollection AddAggregateRebuilder<TAggregate, TRebuilder>(
    this IServiceCollection services)
    where TAggregate : class
    where TRebuilder : class, IAggregateRebuilder<TAggregate>;
public static IServiceCollection AddEventStore<TAggregate, TStore, TRebuilder>(
    this IServiceCollection services)
    where TAggregate : class
    where TStore : class, IEventStore<TAggregate>
    where TRebuilder : class, IAggregateRebuilder<TAggregate>;
```

No speculative provider registrations or discovery. Commands can select writing without history;
maintenance registration supplies its reader. Existing business maintenance Contracts remain
optional module facades and expose no EF or aggregate types.

## Concrete flow and responsibilities

Ordinary command usage remains:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
var aggregate = await store.GetForWritingAsync(id, expectedVersion, token);
aggregate ??= PurchaseOrderAggregate.Create(request);
aggregate.SetLines(lines);
await store.AppendAsync(aggregate, token);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

Append performs one operation: validate its observation, encode all events and the one state
row, then attach/update header/state and add envelopes using native EF. All serialization and
candidate validation finish before tracked mutations. Preserve observed original tokens.
Do not apply pending events to the already evolved aggregate a second time. Keep one accepted
batch per observed stream/context, and final save/commit consumer-owned. Rejected decisions
produce no changes; persistence failure requires a fresh operation and a new decision.

Maintenance selects the separate IAggregateRebuilder and uses the same explicit transaction /
save / commit pattern. It captures one existing filtered header, validates a full retained prefix,
reconstructs an effect-free root, encodes its row, and inserts or copies values into the observed
EF state row. It updates only header ConcurrencyStamp, not Version/time/events. A healthy row
can remain unchanged; the stamp update still gives repair its native optimistic predicate.
No shared admissions, lock-key encoding or pending/acquired access bookkeeping.

Use natural business command names in the active sample: OpenAsync, ReceiveAsync, IssueAsync,
DraftAsync, ChangeLinesAsync and RebuildAsync replace Stage* methods; Changed replaces Staged
result variants, with Proposed payloads unchanged. Explicit caller save/commit still determines
durability, as in normal EF unit-of-work usage. Fixture seeding becomes synchronous Add (no
async admission step). State-row factories use FromState instead of Prepare/PrepareCandidate.
These are concrete consumer naming changes, not a new business contract in the library.

Native save validation still rejects arbitrary state-only mutations. Use one small private
context-bound record of the exact maintenance header/state/transaction so the guard can distinguish
library rebuilding from an accidental direct row update. Integrate this into the native save
guard; no public bypass, PreparedAggregateRebuild class, Stage method or general permit manager.
Normal append validation remains independent. The private record is evidence for a save invariant,
not a substitute for replay/evolution or a new persistence abstraction.

Consumers keep domain rules/reducers, durable aliases/JSONB codecs, ownership/admission, query
filters and final save/commit. They may add pessimistic exclusion around maintenance if their
workload needs it, or pause writes, and own scheduling/scaling/retry policy. A library maintenance
worker remains planned, deferred to keep this rewrite focused. No automatic catch-up, upcasting,
async/multi-stream projections, job table or template event preset.

## Validation audit

| Retain | Remove or consolidate |
| --- | --- |
| Valid supported native mapping, PK/FK and concurrency metadata | Admission resource-model restrictions, provider gate validation and schema/key hashing |
| Explicit native transaction; observation/root association; expected version; one append per observation | Shared/exclusive context lifecycle, lock upgrades, pending admissions, mandatory maintenance configuration in writing |
| Whole-batch event/state encoding before tracked changes; payload identity/schema/key metadata | Public Prepare/Stage handles and repeated after-Prepare mutation/tracker scans across deferred phases |
| Captured prefix completeness/order/time endpoints; reconstructed identity/version/no pending events | Repeated cloning/comparison of all header fields after each trusted adapter call; validate relevant invariants at the operation/save boundaries |
| Main state/head/version/time participation and append-only tracked events in both save paths | Generic multi-mode state preparation and separate prepared replacement orchestration |
| Preserve native original stamp/version, contiguous facts, changed complete key rejection | Custom DI registration atomicity/conflict matrix; use documented native registration semantics |

Two proven redundancies were removed during the authorized readability cleanup: LoadAsync no
longer checks behind-state twice, and prepared repair no longer rechecks Added/Modified after
comparison with the exact captured entry state. The replacement must retain the meaningful
integrity/concurrency failures above; "corner case" alone is not grounds to drop a guarantee.
Adapter side effects, concurrent DbContext use and arbitrary original-token tampering are not
new supported capabilities. Avoid building a sandbox for code executing in the same process.

## Native SQL experiment: new design evidence only

Executed 2026-10-07 against disposable PostgreSQL **18.6**, using a temporary TypeScript harness
with independent persistent psql sessions. The container was removed. No library implementation,
EF integration, new schema in this repository or production concurrency guarantee was tested.

Schema: stream(id, version, stamp uuid), state(id, version, amount), facts(id, version, delta).
The winning header statement is conceptually:

```sql
UPDATE streams SET version = :next_event_version, stamp = :new_stamp
WHERE id = :id AND version = :observed_event_version AND stamp = :observed_stamp
RETURNING version;
```

| Interleaving | Result |
| --- | --- |
| Writer observes wrong same-version amount 99; repair commits amount 10/new stamp; writer issues state/event SQL then attempts its old header predicate | Header affects zero rows. Explicit rollback restores amount 10, version 1 and one retained fact, including rollback of the already-issued state/event SQL. |
| Repair captures version 1/stamp A; writer commits version 2/stamp B and amount 15 | Repair's old header predicate affects zero rows. Version 2/state 15/two facts remain intact. |
| Two repairs capture the same version/stamp | First commits its replacement/new stamp; second header predicate affects zero rows. First replacement remains. |

**Three diagnostics passed.** This corrects the scope of the earlier rejection: an unchanged
event-version predicate or repair-only row lock is insufficient; a token changed by repair is
an alternative to pre-read shared/exclusive admission. The old four diagnostics remain historical
evidence for the old algorithm. These new results justify proposing the alternative, not dropping
its required real EF/consumer tests. No new reusable library mechanism is proven by this proposal.

## Exact replacement map for review

EF/ means src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/.

| Files | Proposed replacement |
| --- | --- |
| EF/IEventStreamRecord.cs; EF/EventSourcingStorageExtensions.cs | Add/map ConcurrencyStamp; keep Version and complete key/position constraints. |
| EF/EventStore.cs | Write-only store; ConfigureInlineState; direct all-encoding-before-EF-change append; no repair/provider references. |
| EF/EventAppender.cs; EF/PreparedEventAppend.cs; EF/EventRecordAdapter.cs → EF/EventRecordMapping.cs | Replace public two-phase operation with internal encoding and native store updates; remove PreparedEventAppend. |
| EF/InlineAggregateAdapter.cs → EF/AggregateStateMapping.cs | Explicit ToAggregate/ToRow mapping; IInlineStateRecord metadata unchanged. |
| EF/InlineProjectionStorage.cs → EF/InlineStateReader.cs plus internal key/binding implementation | Read-only public interface; separate native key metadata, direct state writing; no repair methods in write binding. |
| EF/AggregateRebuilder.cs; EF/PreparedAggregateRebuild.cs; EF/EventStreamReplay.cs; EF/IAggregateRebuilder.cs | Independent typed-row base above; protected reader/reducer integration; remove prepared replacement and extra replay adapter; retain role/result and ReplayedEvent. |
| EF/RequiredInlineStateExtensions.cs; EF/EventStreamRebuildState.cs; EF/EventStreamWriteGate.cs | Ordinary guard plus tiny exact maintenance-write association; remove admission state/gate/rebuild opt-in. |
| EF/EventStoreServiceCollectionExtensions.cs | Separate scoped roles and reviewed helper signatures; standard registration semantics. |
| src/Gloaming.EventSourcing/Gloaming.EventSourcing.Postgres/ (project, implementation and local docs) | Remove the unused admission package; preserve old design/proof context in dated reports, not an empty runtime seam. |
| ModulithFoundry.slnx; sample Inventory/Purchasing project references; family test project; ArchitectureTests project/dependency policies | Remove gate-package references; provider tests reference native Npgsql EF directly. No new provider version/dependency family. |
| Inventory/Purchasing HistoryRows.cs; EventStorageDemo/StorageRows.cs; all test IEventStreamRecord implementations | Add technical stamp; update fixture/header construction without changing literal fact payloads. |
| Inventory/Purchasing stores, InlineViewMapping, queries/filters, registration, rebuilding facades; new per-module StateMapping and Rebuilder bindings | Writing no longer depends on history/provider; maintenance supplies history/reducer separately. Rename mapped CurrentRow types/files to StateRow, remove one-method InlineProjection wrappers, use explicit typed-context reader registration. Business Contracts remain stable. |
| Purchasing/PurchaseOrders/PurchaseOrderSummaryRow.cs; PurchasingHistorySeed.cs; InlineViewMapping.cs; legacy-row-only tests | Remove dormant summary entity/mapping/seed writes and legacy-only proofs. Preserve current query-derived summary and real historical/full-replay equivalence. |
| Inventory/Purchasing/EventStorageDemo Migrations: new AddEventStreamConcurrencyStamp migrations and snapshots; Purchasing new RemoveLegacyPurchaseOrderSummary migration | Apply the actual schema changes, including dropping the unused table; preserve previous migration files and archive. No existing-data compatibility/backfill promise. |
| EventSourcingPostgresTests/{GateTests,GuardTests,RebuildTests,RebuildConsumer,RegistrationTests}.cs; Wholesale/EventPersistenceDemo.Tests; EventStorageDemo.Tests/AppendTests.cs | Replace blocking/admission and public-handle tests with native stamp conflict/rollback/recovery and direct-store proofs; preserve meaningful state/metadata/tenant/JSONB/query assertions. |
| Inventory.Contracts/IStockPositionCommands.cs and IStockPositionRebuilding.cs; Purchasing.Contracts/IPurchaseOrderCommands.cs and IPurchaseOrderRebuilding.cs; corresponding implementations and active call sites | Replace Stage* command/maintenance names and Staged variants as listed above; consumer result semantics/payloads and explicit completion remain. |
| DemoJourneys, RebuildJourney, module seeds and executable READMEs | Direct native completion, independent maintenance and consistent names; remove fixture admissions/async seed wrapper and legacy summary maintenance. Keep demo entry points/literal facts. |
| Family/EF docs; root design/extraction/ES2 brief/report; ADR 0007 | Record reviewed replacement after approval; distinguish stamp conflicts from old gate guarantees and historical executions; worker remains planned. |

No archive/fixture payload, template source, checkpoint or unrelated worktree reset. Previous
active migrations stay; new migrations implement the revised model. The owner permits breaking
active library/sample/schema changes, so no compatibility aliases, dual protocols or old public
handles are retained. Changes remain unstaged; exact complete commit approval is separate.

## Required implementation proofs

Test through IEventStore and IAggregateRebuilder using real PostgreSQL and actual EF native
predicates. Inspect generated UPDATE predicates where necessary. Prove both race directions,
same-version repair, competing repairs/writers, missing-state insertion, SQL order/failure and
rollback (including two saves), cancellation and fresh-context recovery. A failed operation
must not partially persist a header, facts or aggregate state. Keep state-dependent eligibility,
rejected/encoding-failed batch behavior, tenant/complete-key isolation, prefix/schema errors,
JSONB payload lifetime/precision, native Queries/Filters and both save-path guards.

Replace old blocking claims rather than leave tests named as if gates remain. No automatic retry;
conflict means a fresh load/redecision. Run relevant consumer/architecture suites, active build/
format/style/analyzers, native model checks, archive integrity and event-free T1 adoption for
the complete replacement. Assess actual removed classes/configuration/dependencies after the
implementation; do not infer simplicity from line count alone.
