# ES2: explicit single-stream aggregate rebuilding

Historical design/execution record. The active interface is superseded by
[the owner-approved native EF replacement](es2-native-ef-simplification.md).
Renamed-source links lead to current replacements; removed mechanisms link to their replacement scope.

Status: **owner-reviewed and implemented**, 2026-10-07. The owner approved the original ES2
surface, its registration refinement and then the [aggregate-only reduction](es2-aggregate-only-reduction.md).
The latter contains the exact reviewed public changes and replacement file/behavior map.
[Execution results](../reports/es2-single-stream-rebuilding.md) distinguish current aggregate-only
proofs from earlier multi-view executions. Complete changes await line-by-line owner review;
follow-up edits are unstaged and existing index entries remain intact. No commit is authorized.
Checkpoints `1ae13d4`, `8ccf4c8`, `ab85ec9` and `39c1ab3` remain intact.

## Outcome and supported contract

Registered aggregate streams have one required inline aggregate state, always at the event
head after a successful native transaction. Ordinary fetch is strict; missing, behind or
unreadable state does not trigger repair. Raw history-only streams remain independently usable.
Consumers build read shapes through native Queries/Filters or explicit live reconstruction
using existing history readers and reducers. No shared history reader/projector engine is added.

Explicit rebuilding reconstructs an existing identified aggregate from the full retained
prefix and stages one state replacement at the captured head. Missing, behind, unreadable or
wrong same-version state can be replaced without decoding the old body. Ahead state fails.
No events, header version, creation/update times or historical recorded times are rewritten.
One fresh DbContext, one explicit ReadCommitted transaction, one terminal rebuild key.

Keep safe online per-stream admission: shared before writer loading, exclusive before repair
captures its head. Shared writers still compete through native optimistic version predicates;
the gate does not serialize ordinary writers. Admission uses the complete physical key,
including ownership and schema/table. Raw streams retain their separate optimistic contract.

The library owns admission lifecycle, head capture, replay metadata validation, complete
replacement preparation/staging and exact tracked-save validation. Consumers own domain facts,
codecs/reducers, typed contexts, tenancy/authorization, native mappings/migrations, queries,
maintenance orchestration and final SaveChanges/commit. The result describes staging only.

## Concrete consumer usage

Inside the owning module:

```csharp
private readonly IAggregateRebuilder<PurchaseOrderAggregate> rebuilder;

// Establish admitted ownership and authorize maintenance before invoking this operation.
await using var transaction = await database.Database.BeginTransactionAsync(token);
var staged = await rebuilder.RebuildAsync(id, token);
if (staged is null)
{
    await transaction.RollbackAsync(token);
    return NotFound;
}
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
return Rebuilt(staged.Version);
```

Inventory/Purchasing retain their optional module maintenance Contracts. These expose
StageRebuildAsync(id, token) and consumer NotFound/Staged metadata when an external host cannot
name the internal aggregate. They perform module admission; they do not independently save,
commit, schedule or duplicate the library algorithm. Within a module use the generic role.
The executable maintenance journey remains present and owns native completion visibly.

Configure once in the concrete store:

```csharp
ConfigureMainState(new MainState());
ConfigureRebuilding(
    new PostgresEventStreamWriteGate<EventStream>(database),
    new Replay(historyReader));
```

Replay reuses the existing history reader's captured-prefix loading and the consumer reducer.
ReadAsync returns ordered decoded facts with positions/times; Rehydrate returns an effect-free
aggregate at that version with no pending facts. No reducer discovery or generated runtime code.

Register the one mapped state and install ValidateEventStreamChanges in **both** native save
overrides, preserving tenant validation first:

```csharp
model.ConfigureRequiredInlineState<EventStream, StoredEvent, PurchaseOrderCurrentRow>(family);
services.AddEventStore<PurchaseOrderAggregate, PurchaseOrderStore>();
```

The registration helper exposes one scoped concrete store through its two roles. Concrete
constructors select module-specific DbContexts; nothing resolves a bare DbContext. Query-only
composition neither constructs a write store nor acquires admission. Native Set<T>()/Entry
work without named DbSets or context markers. Model annotations alone do not execute guards.

## Reviewed public surface and dependencies

IEventStore<TAggregate> retains GetForWritingAsync and AppendAsync. The narrowed maintenance role:

```csharp
public interface IAggregateRebuilder<TAggregate> where TAggregate : class
{
    Task<AggregateRebuildResult?> RebuildAsync(
        Guid id, CancellationToken cancellationToken = default);
}

public sealed record AggregateRebuildResult(long Version, DateTimeOffset RecordedAt);
```

EventStreamReplay<TAggregate,TEvent,TStream>, ReplayedEvent<TEvent>, EventStreamWriteGate<TStream>
and EventStreamAccess remain the reviewed replay/provider seam. The Postgres package supplies
transaction advisory admission. Shared EF references Relational, DI abstractions and the
package-free aggregate core; only the optional provider references Npgsql EF. No worker,
Marten dependency, ambient transaction or code generation.

Removed secondary-view surface: ConfigureRequiredProjection, InlineEventProjection and the
isMainState registration flag. IInlineProjectionRebuilder/InlineRebuildResult are replaced
by the aggregate role/result; ProjectionCount disappears. The store forwards maintenance to
a dedicated internal implementation; normal append has no rebuilding flag or participant list.
PreparedEventAppend retains the independent raw path and complete encode-before-track guarantee.

Purchasing now derives its current summary Contract from checked aggregate state. Its legacy
summary mapping/table/migrations/seed rows and literal fixtures remain, but ordinary commands
and current queries no longer depend on or maintain those rows. This single-ID derivation is
not proof of server filtering/paging on calculated totals; future list queries need translated
mapped expressions and PostgreSQL assertions.

## Errors, recovery and provider limits

Configuration, model/provider/context, transaction/isolation and lifecycle violations fail
with InvalidOperationException. Replay/header/rehydrated metadata, ahead maintenance rows and
missing candidates fail with InvalidDataException. Missing scoped stream returns null.
Codec/domain failures and SQL constraints, concurrency, cancellation and connection errors
retain their native meanings. Do not blanket-translate unrelated faults into version conflicts.

Prepare the full replacement before tracking. Keep native original state-version values;
updating a same-version row still issues SQL. The private replacement association permits
only the exact header/state/key/version/time/transaction shape. Header/event changes, omissions,
unrelated tracked entries and unprepared state-only writes fail before SQL. Consumers receive
no public validation bypass. Semantic body tampering by arbitrary same-process code is outside
this tracked metadata contract.

After cancellation, SQL failure or rollback, dispose context/aggregate and retry through a
fresh operation. No automatic retry, tracker recovery or ambiguous-commit resolution.
PostgreSQL 18.6 with the pinned native provider is the only proven runtime; the selected gate
rejects non-Npgsql providers. ReadCommitted avoids capturing an old snapshot while waiting.
External/bulk/SQL/old-binary writers must cooperate with admission to receive its protection.
Custom key collations/equality and rolling schema/key-encoding changes need separate coordination.
Full replay materializes one history and decoded state, O(events + state), without a memory,
throughput, resumability or business-key uniqueness guarantee.

## Verification and preservation

Meaningful consumer proofs cover state-dependent decisions, rejected batches, heterogeneous
facts, state/head consistency, competing appends, writer/maintenance exclusion, prefix integrity,
native sync/async save guards, same-version replacement, missing/corrupt state, SQL failure,
cancellation, rollback and fresh recovery. Use real PostgreSQL for transaction/concurrency claims.
Current Purchasing tests prove derived summaries ignore damaged/missing legacy rows and commands
leave them unchanged. Existing executable entry points and authored output remain.

Run relevant provider, Wholesale, independent counter, architecture and existing consumer checks,
active build/format/style/analyzers, native pending-model checks, frozen archive verification and
T1's event-free external adoption. Do not regenerate migrations or change fixtures/archive.
The report identifies each new execution and replaced historical secondary guarantee.

## Deferred work

A library maintenance worker remains **planned and needed eventually**; it is deferred to
simplify this change. Consumers can implement reconciliation around the explicit rebuilder and
own scheduling, scaling and online/offline orchestration while respecting the supported gate
contract. A worker needs its own reviewed queue/restart/retry/progress contract and proofs.

Catch-up is deferred: current inline state must already be at the head, and full maintenance
handles backfill/repair. Snapshot/async state allowed to lag may later justify suffix evolution.
Historical schema reading/upcasting remains a priority. Assess native EF joins/filters before
extracting secondary/multi-stream projection orchestration; no such runtime is present today.
Discovery, revisions/cutover, global feeds, messaging/audit, RLS, cross-module transactions and
template event presets remain excluded. See the self-sufficient
[local capability catalog](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/docs/capabilities.md).

[Earlier native SQL diagnostics](../reports/es2-rebuild-design.md) explain why repair-only
locking was rejected. The retained TypeScript reproducer is design evidence alongside the
family PostgreSQL integration suite, not a production worker or replacement for those tests.
