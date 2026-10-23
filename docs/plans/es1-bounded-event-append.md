# ES1: bounded event append with a state-dependent consumer

Status: the owner approved the initial interface/scope, then requested and approved the
aggregate/store replacement on 2026-10-06 ("good. make the changes."). The replacement is
implemented with executable adoption and new verification; [the slice report](../reports/es1-bounded-event-append.md)
records results and limits. The subsequent [provided store surface](es1-library-write-store.md) governs current write
loading and required-state integration. The replacement sections below retain the earlier
appender review history; the lower-level prepared append remains its underlying mechanism.

Approval does not authorize a commit. Revision edits remain unstaged, preserving pre-existing
index entries. Follow [the repository workflow](../conventions/repository.md),
[.NET conventions](../conventions/dotnet.md), [design](../design.md),
[the current plan](library-extraction.md) and applicable ADRs.

## Outcome

Demonstrate one useful optional event capability: a consumer loads state corresponding to
an observed stream version, makes a decision that actually depends on that state, and stages
an accepted event batch against that version. A justified library module concentrates
repeated technical append rules; the consumer keeps domain decisions and native persistence
completion. A materially different adopter exercises the same interface independently.

Focus extraction on accepted-batch append. Reuse existing captured-history or validated
inline-state loading; do not simultaneously extract a new history reader or projector engine.
The full loading/decision/append journey is the consumer proof, not a requirement that every
step become a library interface. If inspection shows the proposed append module merely
relocates small native calls, present that finding and a narrower revised scope before
adding abstraction. A package is not owed by the candidate inventory.

## Owner follow-up: inline state, queries and future capabilities

On 2026-10-07 the owner requested applying these directions. The
[exact follow-up interface/file proposal](es1-inline-state-enforcement.md) covers registered
main-state save enforcement, appender simplification, native filtered reads and JSONB proofs.
That narrow recommendation was superseded by further store feedback. On 2026-10-07 the
owner endorsed IEventStore<TAggregate> and authorized concrete changes. The
[provided store surface and exact map](es1-library-write-store.md) now describe the implemented
revision for line-by-line review; [new executions](../reports/es1-library-write-store.md) are
separate from the earlier appender results. Sections below retain earlier proposal/review
history and are superseded where the store brief changes ownership or method names.

The owner confirmed transactional main inline state for aggregate writes and native EF read
queries through module-local `{Aggregate}Queries` / `{Aggregate}{Specific}Query` and reusable
`{Aggregate}Filters`. [The follow-up catalog](event-sourcing-capabilities.md) records these
directions, the archive/current event registries, JSONB mapping guidance and deferred
capabilities with their remaining proof obligations.

This was initially a discussion direction; the later owner authorization produced the
provided store revision. Earlier local stores included views but the appender could stage
events/header alone. The new configured native save checks enforce tracked inclusion. The owner requires that
stronger guarantee for registered aggregate writes and permits explicit raw streams, preserving the
history-only counter; [ADR 0006](../adr/0006-transactional-main-inline-state.md) records the
decision. The concrete implementation is now available for owner review; its new proofs and
tracked-write limits appear in the provided-store report.

## Existing evidence and preservation

- E4 codec (`2a49ef3b`) and E5.1 range integrity (`4cc12a1`) already hide reusable mechanics.
- E5.2.1 native history consumers (`4f4d5b2`) and E5.2.2 append/E5.3 storage registration
  (`abcd370`) establish the existing EF/PostgreSQL protocol and its limits.
- E6.1 (`1ae13d4`) adds sample-owned inline state and required views; no shared append or
  projection runtime was extracted. Use it to compare consumer obligations.
- T1 (`8ccf4c8`) proves a separate event-free generated composition. Preserve its omission
  guarantee; adding this event capability must not make it compulsory.

Read the corresponding plans/reports as dated evidence. At ES1's starting checkpoint, command modules repeated
transaction preconditions, observed-version handling, event positions, stream advancement
and preparation. Their decisions chose receipt/line events from requests before
loading state, so a real state-dependent consumer is needed for this comparison.

Preserve existing source, literal fixtures, migrations and independent-adoption proofs.
Do not remove demos, reset event checkpoints or change the frozen archive. Any necessary
replacement needs an explicit file/behavior disposition and a reviewed scope; the earlier
owner instruction remains **no deletion yet**. Small demos may still be useful standalone
proofs. Cleanup is not a prerequisite for ES1.

## Contract review before implementation

Inspect the actual callers, then present a concrete proposal in this plan containing:

1. A small consumer usage example showing load, state-dependent decision, accepted-batch
   preparation, native transaction, staging, save and commit. Make the ordering visible.
2. Proposed public types and errors, actual dependencies, registration/configuration and
   the exact technical rules moved out of each caller. Choose the smallest supported seam;
   do not reserve package projects or invent interchangeable providers.
3. Ownership of DbContext, connection, state/version observation, serialization, event
   identities/timestamps, staging, additional participants, cancellation, retries and disposal.
4. State/expected-version consistency, competing creation and append behavior, rejected-batch
   behavior, fresh-context recovery, empty-batch and repeated-call semantics. Support or
   explicitly reject each case rather than hiding it in an undocumented precondition.
5. A primary state-dependent sample rule, its module ownership and domain meaning, and a
   second adopter that varies a real obligation. A tenant-free standalone consumer versus
   the tenant-owned sample is a possible comparison; renaming domain types alone is weak evidence.
6. Existing files to retain or adapt, schema compatibility, changed assurance and tests that
   demonstrate leverage at the public interface. No guarantees transfer automatically.

Obtain owner review of the proposed new/changed library interface before implementing it,
as required by the extraction/promotion gate. Keep proposals here; use an ADR only when
a hard-to-reverse architecture choice has actually been settled. Existing checkpoint approval
does not authorize a new interface or commit. After review, implement this single capability
with consumer usage and relevant proofs in the same work session.

## Historical interface reconsideration after owner feedback

This section records the recommendation before the exact replacement was reviewed. The
approved implementation is specified in the next section; provisional statements here
are preserved as review history.

The owner requested a deeper inspection of the archived Purchasing aggregate, persistence
store and IssuePurchaseOrderHandler, and questioned per-call factories, argument count and
the JsonElement dependency in IStoredEventRecord. The first implementation concentrates a
technical protocol, but its callers still supply a header, a separately supplied expectation,
an event list, a timestamp and an envelope factory. It does not provide the aggregate-oriented
consumer journey that made the archived implementation useful. Source-line concentration
and passing protocol tests do not settle that interface concern.

### What the archived implementation actually does

- [IssuePurchaseOrderHandler](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/IssuePurchaseOrder/IssuePurchaseOrderHandler.cs)
  opens a native transaction, loads an aggregate through the store with the command's expected
  version, calls Issue(), stages the aggregate, then explicitly saves and commits. Neither
  event arrays nor JSON envelopes appear in the business operation. The handler still supplies
  actor/Organization and a TimeProvider timestamp; reducing those append arguments would be
  a deliberate change from the archive, not a behavior inherited from it. Archived audit
  participation is excluded from this ES1 revision.
- [PurchaseOrderAggregate](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderAggregate.cs)
  captures Id and ExpectedVersion on loading. Its methods invoke the module's decider, evolve
  the whole candidate, validate the final candidate, then update State, Version and Pending.
  A failed candidate is not accepted. Purchasing captures ExpectedVersion as a fixed value;
  Inventory derives it from Version minus pending count. A shared mechanism should capture
  the observed version once and use checked arithmetic before changing any accepted field.
- [PurchaseOrderStore](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderStore.cs)
  checks the stream against the command expectation and required write-state version. It
  constructs the aggregate from that state, encodes pending events, fills envelope positions
  and invokes explicit required inline participants. It is a concrete module-local store.
  The inspected snapshot contains no shared generic event store or generic aggregate base.
  A reusable typed store would be a new extraction of those repeated obligations.
- [Inventory's aggregate](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/StockPositionAggregate.cs)
  makes the distinction between AcceptDecision and ApplyHistorical explicit. Historical
  application advances reconstructed state/version but never collects pending facts or runs
  current candidate policy. Purchasing's [event reader](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderEventReader.cs)
  instead folds its reducer directly and returns reconstructed state/version/time. Both
  loading paths are legitimate; sharing aggregate bookkeeping does not require replacing them.
- [Purchasing's inline projector](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderInlineProjection.cs)
  advances an aggregate-shaped write model and an independently reduced summary. The aggregate
  candidate cannot become the mutable baseline of both projectors. Required-view selection,
  reduction and native staging remain explicit consumer responsibilities.
- [Purchasing's existing persistence tests](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/tests/PersistenceTests/PurchaseOrderPersistenceTests.cs)
  include literal historical facts above current command limits, editing without event SELECT,
  competing writers, participant faults and clock regression. These are inspected historical
  tests, not new results for a revised interface. The archive's handler Clear() recovery and
  broad conflict classification are not recommendations to weaken ES1's fresh-context recovery.

### Recommended direction, pending interface review

Use one aggregate per stream as the write contract: stream identity, immutable observed
version, current candidate state/version and a read-only ordered pending batch belong together.
A small reusable aggregate bookkeeping implementation may support consumer-defined operations,
deciders, evolution and final candidate validation. It should accept the complete batch only
after evolution, policy and checked version arithmetic succeed. Historical application has a
separate path and leaves Pending empty. Domain event types require no JSON/EF fields.
An unchanged decision may leave Pending empty; the module returns its unchanged result after
validating the expectation during loading. It creates no empty stream or append. This differs
from treating an explicitly submitted empty technical batch as a valid write, and needs an
aggregate/store proof if supported by the revised interface.

An aggregate contract need not force an inheritance hierarchy. Decide separately whether to
require the contract for writing and whether to provide a convenience base/composed helper.
Read-side projections need not be aggregates. Multiple aggregate families may still share a
table pair with explicit stable StreamType registration; one aggregate per stream does not
mean one family per table or automatically discover a CLR type from history.

Move constant append dependencies to a configured, typed persistence adapter/store: native
DbContext, clock, event-family identity/serialization and row ownership/customization. The
normal operation should pass the aggregate and cancellation token, rather than reconstructing
an envelope factory or resupplying its version and pending list. A named, typed adapter may
still implement encoding/custom fields; those extension points belong in persistence setup,
not anonymous functions in every command path. Merely wrapping today's arguments in an options
record or adding a thin store forwarding to Prepare would not resolve the criticism.

The library assigns envelope GUIDs, contiguous positions and one UTC recorded timestamp per
batch. It advances the header from the aggregate's captured observation and reports that
timestamp/version to explicit required-view preparation. The clock is supplied once through
TimeProvider; regression should continue to reject the append, rather than silently clamp
time or claim commit-time ordering. It preserves the accepted batch order: inferring semantic
event order by sorting types, GUIDs or timestamps would be incorrect. Domain event time, if
needed, is separate from the append's RecordedAt.
Preparation must still complete encoding and required-view candidates before tracking a
proposal. The prepared append remains bound to the observed header, original native context
and transaction; competing commits are rejected at native save. Pending events are not cleared
as an assertion of durability during staging, and a failed save/rollback requires a fresh
context, freshly loaded aggregate and a new decision. No automatic commit or retry is added.

The target business usage is:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
var order = await store.LoadForWritingAsync(command.Id, command.ExpectedVersion, token);
if (order is null)
    return NotFound();
order.Issue(); // Module-owned decider, evolution and complete-candidate policy.
await store.StageAppendAsync(order, token); // Configured persistence, including explicit views.
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

This illustrates the module-store interface, not yet a selected generic library signature.
The first recommendation is to keep existing state/history loaders and required-view code
module-owned, extracting aggregate bookkeeping and configured append only. A shared
LoadForWriting store needs a separately reviewed definition of how inline state and history
loading vary; it must not silently add a reader/projector engine to the original bounded scope.

### JSON placement and dependency consequences

JsonElement is not inherently required by event-sourced aggregates or append/version mechanics.
It is required by today's explicitly JSON storage/codec choice. IStoredEventRecord is already
a persistence contract; the leak becomes problematic when business operations construct those
rows or JSON payloads themselves. Hide them behind the typed persistence adapter first.

Keep the existing JSON row interface and migrations for this bounded revision unless a real
alternate payload adopter justifies changing them. Removing Payload from the interface means
mapping and lifetime validation must move into a JSON-specific adapter. Replacing it with
object weakens the contract, while IStoredEventRecord<TPayload> carries another generic through
mapping, codec and append without demonstrating a second representation. Neither is selected.

Package placement remains part of the next proposal: aggregate bookkeeping must not depend on
EF/JSON or business Contracts, and making the existing EF storage segment depend on the codec
would change its independent-adoption promise. A configured JSON adapter can compose the
existing segments without forcing a codec dependency onto the tenant-free direct-JSON adopter.
No new package/base class/dependency is introduced by this document.

### Revision gate and preservation

The owner's instruction to proceed settles the recommended direction: require a write
aggregate contract and retain module-owned loaders. The exact public types, dependencies,
errors, configuration, replacement file map and new proofs below are now presented for review
before changing C# interfaces. The old protocol tests remain evidence for the first
implementation; they cannot certify the new aggregate/store lifecycle.

The implementation and archive are unchanged during this review. Preserve existing staged
and unstaged edits as found; this reconsideration changes only the brief. Any replacement of
the two ES1 types or their consumer tests must be included in the reviewed concrete revision
scope. Native final save/commit, tenant admission, business Contracts, required-view policy,
independent adoption and T1's event-free composition remain requirements. Deferred capabilities
remain excluded.

## Concrete aggregate-based replacement for interface review

Owner-approved for implementation on 2026-10-06 after the ApplyChanges/Evolve and prepared-batch
discussion ("good. make the changes."). The signatures and replacement scope below govern the
revision. Implementations remain subject to line-by-line review. [The revised report](../reports/es1-bounded-event-append.md)
records actual results; initial interface/results below are dated historical evidence.

This replaces the first ES1 append surface rather than offering a second path beside it.
It implements the approved direction through a package-free aggregate contract/bookkeeping
segment and a configured native EF appender. Module stores own their existing load and
required-view code. No generic history loader, projector engine or transaction wrapper is added.

### Exact new and changed library interfaces

In the new `ModulithFoundry.EventSourcing` namespace/project:

```csharp
public interface IEventSourcedAggregate<out TEvent> where TEvent : class
{
    Guid Id { get; }
    long ExpectedVersion { get; }
    long Version { get; }
    IReadOnlyList<TEvent> PendingEvents { get; }
}

public abstract class EventSourcedAggregate<TState, TEvent>
    : IEventSourcedAggregate<TEvent>
    where TState : class
    where TEvent : class
{
    protected EventSourcedAggregate(Guid id, long observedVersion, TState? observedState);

    public Guid Id { get; }
    public long ExpectedVersion { get; }
    public long Version { get; private set; }
    public TState? State { get; private set; }
    public IReadOnlyList<TEvent> PendingEvents { get; }

    protected void ApplyChanges(IReadOnlyList<TEvent> events);
    protected abstract TState Evolve(TState? state, IReadOnlyList<TEvent> events);
    protected abstract void ValidateCandidate(TState candidate);
}
```

These are signatures, with implementation bodies deliberately omitted for review. The
interface is required for event-sourced append; inheriting the bookkeeping class is optional.
The class makes Id/ExpectedVersion fixed, starts with no pending events, and exposes an actual
read-only pending view rather than the backing mutable List. Constructor arguments must be a
nonempty ID, a nonnegative version, and null state exactly for a new/version-zero aggregate.
Existing state is not checked against today's domain policy at construction.

ApplyChanges snapshots the proposed facts, rejects null entries, checks the complete proposed
version with checked arithmetic, evolves a local candidate through the complete batch and invokes ValidateCandidate once
for the final state. Only then does it replace State, advance Version and add pending events
in their accepted order. Empty decisions leave state/version/pending unchanged. Domain
operations, deciders, Evolve and ValidateCandidate are consumer implementations, required to
be pure and to use immutable state/facts; the base cannot undo their arbitrary side effects.

There is no Replay method that treats history as a new decision. Existing readers continue
to validate selected metadata, decode and apply the existing domain reducer, then construct
the aggregate with the resulting state and captured version. That constructor collects no
pending facts and does not invoke ValidateCandidate. The same domain reducer is used by
Evolve for new candidates. This supports both inline-state initialization and rehydration
without extracting another reader or duplicating the existing range utility.

The owner's subsequent naming/reuse discussion is reflected here: ApplyChanges means the
aggregate operation that updates accepted state/version/pending after candidate validation;
Evolve means pure calculation of the next state. Evolve receives the complete batch so the
existing Purchasing reducer need not copy the line dictionary once per event. These names
are repository choices, not a claim of universally prescribed DDD terminology.

Keep the pure reducer in the owning module, outside the aggregate class. The aggregate's
Evolve hook delegates to it, and existing historical reconstruction uses the same reducer.
Only aggregate business methods can invoke protected ApplyChanges. Sharing an internal pure
calculation does not expose aggregate setters or bypass decision eligibility. Two views of
the same state can share that reducer, each starting from its own immutable baseline. Views
of different state shapes may share meaningful domain calculations, but keep their distinct
reducers. Purchasing's summary must continue evolving its own line amounts, rather than
deriving its next state from the proposed aggregate and losing the independent-view proof.
No generic projector/reducer interface is added merely to wrap these static functions.

In the existing `ModulithFoundry.EventSourcing.EntityFrameworkCore` namespace/project:

```csharp
public abstract class EventRecordAdapter<TEvent, TStream, TStoredEvent>
    where TEvent : class
    where TStream : class, IEventStreamRecord
    where TStoredEvent : class, IStoredEventRecord
{
    public abstract string StreamType { get; }
    public abstract TStoredEvent CreateRecord(TEvent fact, TStream stream);
}

public sealed class EventAppender<TEvent, TStream, TStoredEvent>
    where TEvent : class
    where TStream : class, IEventStreamRecord
    where TStoredEvent : class, IStoredEventRecord
{
    public EventAppender(
        DbContext database,
        EventRecordAdapter<TEvent, TStream, TStoredEvent> records,
        TimeProvider timeProvider);

    public PreparedEventAppend<TStream, TStoredEvent> Prepare(
        TStream observedStream,
        IEventSourcedAggregate<TEvent> aggregate,
        CancellationToken cancellationToken = default);
}

public sealed class PreparedEventAppend<TStream, TStoredEvent>
    where TStream : class, IEventStreamRecord
    where TStoredEvent : class, IStoredEventRecord
{
    // Internal constructor; produced only by validated appender preparation.
    public long ExpectedVersion { get; }
    public long NextVersion { get; }
    public DateTimeOffset RecordedAt { get; }
    public void Stage(CancellationToken cancellationToken = default);
}
```

`EventAppend` and its static delegate-based Prepare are removed. The adapter is configured
once in owning persistence code. It knows the explicit durable StreamType and populates a
fresh detached row's EventName, SchemaVersion, Payload and ownership/custom fields. Existing
JsonEventCodec is used by the two module adapters; the independent adapter uses direct
System.Text.Json. Adapter code does not set GUIDs, positions, header fields or recorded time.
Returning mutable rows is confined to this persistence extension point; adapters must not
retain/mutate them or change the observed header/aggregate/context.

Prepare verifies aggregate/header ID and observed-version agreement, and requires
aggregate.Version == checked(aggregate.ExpectedVersion + pendingCount). It rejects an empty
technical append. It materializes pending facts, samples the configured clock once, preserves
fact order, generates distinct event GUIDs, fills positions/time and clones JSON payloads.
Event GUIDs use Guid.NewGuid(); they provide event identity, not ordering or command deduplication.
UTC/nonregression, complete mapped keys, configured family identity, schema/name/payload and
freshness checks precede tracking. No caller supplies expected version, event array, envelope
identity, timestamp or per-call delegate to this method.

The prepared result captures the aggregate's identity/version/pending sequence as well as the
original header/context/transaction. Changes to aggregate version or pending references after
Prepare invalidate Stage; preparation also checks that adapters did not change the captured
header or aggregate. Mutation inside event/state objects remains unsupported. Stage retains
native original-version predicates, complete-key checks and one staged batch per stream key
per context before/after save. It never clears pending facts as a claim of committed success.

PreparedEventAppend is a single-use prepared write handle for the entire batch. It privately
holds the actual encoded envelope rows with assigned IDs/positions/time and cloned payloads,
plus the captured header/aggregate observation and context/transaction binding. It is neither
one domain event nor a successful-commit receipt. Prepare constructs/validates that write
proposal; Stage places its header/envelopes in EF tracking; the caller's SaveChanges writes
SQL and Commit makes the transaction durable. Neither raw rows nor mutation setters are
exposed through the handle. Required views are prepared separately by the owning store using
the same RecordedAt/NextVersion, then staged explicitly before the caller saves.

### Consumer usage and configured dependencies

Module Contracts remove RecordedAt input from all five stock/purchase-order staging methods:

```csharp
Task<StockPositionChangeResult> StageOpenAsync(OpenStockPosition request, CancellationToken token);
Task<StockPositionChangeResult> StageReceiptsAsync(ReceiveStock request, CancellationToken token);
Task<StockPositionChangeResult> StageIssuesAsync(IssueStock request, CancellationToken token);
Task<PurchaseOrderChangeResult> StageDraftAsync(DraftPurchaseOrder request, CancellationToken token);
Task<PurchaseOrderChangeResult> StageLinesAsync(ChangePurchaseOrderLines request, CancellationToken token);
```

Command request/result types and their business meaning stay otherwise unchanged. Expected
version remains a command input: it protects client intent and is checked during loading,
then captured once on the aggregate. Module registration supplies TimeProvider.System only
when the consumer has not registered a clock. Tests/demos configure an operation clock in
their composition; business methods receive no persistence timestamp.

Commands inject their concrete module store. The Inventory issue path validates command
shape, loads the observed aggregate through existing header/view checks, invokes TryIssue,
returns InsufficientStock on rejection, then stages the accepted aggregate. It does not build
EF envelopes, JSON, time or version arithmetic. The store prepares the library append, uses
its RecordedAt/NextVersion to prepare every required view, then explicitly calls append.Stage
and the existing module projection.Stage. Both preparations finish before either staging call.
The equivalent Purchasing store retains its independently evolved summary.

The module Store remains the business-facing interface for its stream. EventAppender is the
configured technical mechanism used inside that store. Interface segregation does not require
handlers to talk directly to an appender or replace LoadForWriting/StageAppend with EF rows.
Module Store owns state-loading and required-view choices; the appender concentrates technical
append. A generic load-and-stage Store remains outside the selected bounded revision.

The module store keeps the observed header/views associated with its loaded aggregate and
requires the same native transaction through preparation. Reload after a rejected decision
is allowed while nothing is staged; a previously superseded aggregate cannot be staged.
No shared identity map or reader lifecycle is introduced. Ordinary public Contract use is:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
var result = await commands.StageIssuesAsync(
    new IssueStock(id, expectedVersion, [new StockIssue(4), new StockIssue(3)]), token);
if (result is StockPositionChangeResult.Staged)
{
    await database.SaveChangesAsync(token);
    await transaction.CommitAsync(token);
}
else
{
    await transaction.RollbackAsync(token);
}
```

The domain/store path inside that module is:

```csharp
var aggregate = await store.LoadForWritingAsync(request.Id, request.ExpectedVersion, token);
if (aggregate is null)
    return new StockPositionChangeResult.NotFound();
if (!aggregate.TryIssue(quantities, out var requested))
    return new StockPositionChangeResult.InsufficientStock(aggregate.State!.OnHand, requested);
var recordedAt = store.StageAppend(aggregate, token);
return new StockPositionChangeResult.Staged(
    aggregate.State!.ToHistory(aggregate.Id, aggregate.Version, recordedAt));
```

StageAppend is synchronous because its preparation/staging performs no database I/O. Native
loads remain asynchronous, as do the caller's save/commit. The store's body explicitly retains:

```csharp
var append = appender.Prepare(observed.Stream, aggregate, token);
var next = PrepareRequiredView(aggregate.State!, append.NextVersion, append.RecordedAt);
// Purchasing also prepares its summary from the summary's own committed baseline.
append.Stage(token);
projection.Stage(observed.Current, next);
return append.RecordedAt;
```

The new core project has no package/framework/project dependency. The existing EF segment
adds one declared reference to that core, keeping only EF Relational as its runtime package.
No codec, History, tenancy, actor, messaging or module Contracts dependency is added there.
Inventory/Purchasing and the independent counter explicitly reference the core they use.
The counter still uses its local captured-prefix reconstruction and direct JSON adapter,
without tenancy, codec library or required views. Its typed domain facts/state are free of JSON.

IStoredEventRecord, its JsonElement Payload, the mapping utility and existing row models remain
unchanged. This is a JSON persistence interface; domain aggregates and commands do not depend
on it. No payload generic or alternate representation is introduced. No new external package
version, provider promise, runtime discovery, service registry or template event preset is added.

### Errors, guarantees and changed assurance

| Condition | Proposed outcome |
| --- | --- |
| Invalid aggregate identity/version/state pairing or null proposed facts | Standard argument exception; no accepted aggregate mutation. |
| Decision rejection, reducer failure, final policy failure or version overflow | State/version/pending remain at the previous accepted baseline, assuming pure immutable consumer operations. Consumer domain errors propagate unchanged. |
| Empty decision | No aggregate change. A module may return unchanged; attempting a technical append with no pending facts still fails. |
| Header/aggregate identity mismatch, malformed aggregate count/version or invalid adapter metadata | Argument/InvalidOperation exception before tracking; no header/view/envelope mutation in the context. |
| Captured version differs from header | Native DbUpdateConcurrencyException at preparation; module preflight normally presents Conflict first. |
| Regressing clock or invalid persisted time | Existing argument/integrity exception behavior before tracking; do not invent a later time. |
| Missing transaction, repeated key, changed aggregate/header or replacement transaction | InvalidOperationException before library staging. |
| Cancellation before preparation/staging | OperationCanceledException before new tracked work. Native save/commit cancellation keeps native semantics. |
| Adapter encoding/customization failure | Original error propagates; accepted in-memory aggregate remains a proposal with pending facts. Dispose the operation rather than claiming domain acceptance was undone. |
| Required participant preparation failure | No append/view tracking. Native tracking/save faults or rollback require disposal and fresh loading/redecision. |
| Competing writer | Native header concurrency or exact existing position/creation constraints at save; no retry or fault translation in the library. |

New tests must prove these contracts through aggregates and the configured appender, including
consistency between the loaded decision state and its observed version. The first implementation's
373 passing cases are dated evidence for its interface; revised results are recorded separately in the slice report.
Arbitrary producer side effects, state/event mutation, tracker clearing/detaching, concurrent
context use and ambiguous commit recovery remain unsupported. PostgreSQL remains the proved
provider; native final save/commit and required-participant selection remain consumer-owned.

### Exact replacement file and behavior map

Paths below are repository-relative. Existing files are adapted unless expressly marked new
or removed. This is the additional revision scope, preserving unrelated staged/unstaged work.

| Files | Change |
| --- | --- |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing/Gloaming.EventSourcing.csproj`, `IEventSourcedAggregate.cs`, `EventSourcedAggregate.cs`, `README.md` (new) | Package-free write aggregate contract and atomic candidate/pending/version bookkeeping, with its purity and hydration contract. |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventAppend.cs` (removed) | Replace the uncommitted static delegate-based surface; do not retain a compatibility backdoor. |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventAppender.cs`, `EventRecordAdapter.cs` (new) | Configured typed append and one persistence customization/encoding seam. |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/PreparedEventAppend.cs`, `README.md`, `ModulithFoundry.EventSourcing.EntityFrameworkCore.csproj` | RecordedAt output, captured aggregate validation, new core reference and revised contract guide; preserve native staging mechanism. |
| `samples/Wholesale/modules/Inventory/Inventory.Contracts/IStockPositionCommands.cs`, `samples/Wholesale/modules/Purchasing/Purchasing.Contracts/IPurchaseOrderCommands.cs` | Remove per-command RecordedAt arguments; retain request/result definitions and meanings. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionAggregate.cs`, `StockPositionStore.cs`, `StockPositionEventRecordAdapter.cs` (new) | Own domain operations/evolution/policy, reuse checked inline loading, prepare required view, and configure codec/ownership once. |
| `samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderAggregate.cs`, `PurchaseOrderStore.cs`, `PurchaseOrderEventRecordAdapter.cs` (new) | Same bookkeeping/append adoption with explicit current/summary loading and independently prepared summary. No new issuance business behavior in this revision. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs`, `StockPositionDecisions.cs`; `samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderCommands.cs`, `PurchaseOrderDecisions.cs` | Commands delegate persistence to module stores and business operations to aggregates; retain or adapt existing pure decision rules. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionEvolution.cs`; `samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderEvolution.cs` | Rename the pure batch Apply method to Evolve for consistency; reuse it from aggregate candidate calculation and the existing Rehydrate method. Preserve historical meaning and the existing batch implementation. |
| `samples/Wholesale/modules/Inventory/Inventory/InventoryRegistration.cs`, `Inventory.csproj`; `samples/Wholesale/modules/Purchasing/Purchasing/PurchasingRegistration.cs`, `Purchasing.csproj` | Register module stores/default clock and explicitly reference the core. Native context lifetimes stay unchanged. |
| `src/Gloaming.EventSourcing/samples/EventStorageDemo/CounterAggregate.cs`, `CounterStore.cs`, `CounterEventRecordAdapter.cs`, `CounterEvents.cs` (new) | Typed counter decisions/pending facts; local history loading and direct JSON encoding in persistence. |
| `src/Gloaming.EventSourcing/samples/EventStorageDemo/CounterCommands.cs`, `CounterAppendJourney.cs`, `CounterClock.cs` (new), `EventStorageDemo.csproj`, `README.md` | Remove timestamp parameters, use configured store/clock and reference the core. Retain committed output/history policy and original DemoJourneys. |
| `samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/DemoComposition.cs`, `AppendJourneys.cs`, `StockIssueJourney.cs`, `README.md`; `DemoClock.cs` (new) | Configure demo clock and invoke simplified Contracts; preserve original stream IDs, fixtures and all ten expected output lines. |
| `samples/Wholesale/IntegrationTests/EventPersistence/AppendTests.cs`, `AppendTests.InlineViews.cs`, `AppendTests.StateDependent.cs`; `TestClock.cs` (new) | Control clock in operation setup; retain all PostgreSQL assertions and state-dependent command proofs, adding aggregate/store lifecycle cases where needed. |
| `src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/AppendTests.cs`; `CounterClock.cs` (new) | Replace static-Prepare test calls with real typed adapters/aggregates. Preserve supported rollback/ownership/concurrency/payload-lifetime assertions; generate identity assertions rather than submit now-unowned envelope IDs. |
| `src/Gloaming.EventSourcing/tests/EventSourcingTests/EventSourcingTests.csproj`, `AggregateTests.cs` (new) | Pure core-interface proofs: whole-candidate failure, later event/null/overflow faults, ordered multiple decisions, immutable captured version, empty decisions/read-only pending and historic initialization without current policy. |
| `tests/ArchitectureTests/ArchitectureTests.csproj`, `AdoptionDependencyTests.cs`, `AssemblyDependencyTests.cs`, `SampleModuleBoundaryTests.cs` | Prove package-free core, exact EF-to-core dependency and retained independent adoption/no business dependencies. |
| `ModulithFoundry.slnx`, `.github/workflows/ci.yml` | Add core/test projects and core tests to the existing active context CI lane. Preserve archive lanes and current PostgreSQL coverage. |
| `README.md`, `docs/design.md`, `docs/development.md`, `docs/plans/library-extraction.md`, this brief, `docs/reports/es1-bounded-event-append.md`, `samples/Wholesale/modules/README.md` | Describe the revised surface, dependency cost, ownership, new versus superseded assurance and implementation review map. |
| `docs/adr/0005-aggregate-write-contract-and-native-append.md` (new after approval) | Record the required write aggregate contract and package-free core/EF dependency once this placement is reviewed. It does not select a generic loading/projector engine. |

The two row interfaces, mapping/options, all database contexts/models, required-view classes,
domain event aliases/schema/payloads, historical reader behavior and reducer semantics, literal fixtures, migrations,
snapshots, original authored setup journeys and console expected outputs remain unchanged.
No archived source or template/tooling source changes. Test cases submitting empty/duplicate
envelope GUIDs are replaced with generated-identity proofs because GUID population moves to
the library; existing native collision/fault classification assertions remain.

### Required revised verification and stop

Run the pure core tests plus both complete PostgreSQL append/history suites and architecture
checks. Add preparation/staging tests for mismatched aggregate/header IDs/versions, forged
pending-count/version, changes after preparation, adapter failure, generated identities,
one-clock-sample batch timestamps and clock regression. Prove domain rejection leaves state,
version and pending unchanged, and replayed old facts above today's policy remain readable
with no pending facts. Retain deterministic competitors between header/state/history reads,
two eligible writers, whole-batch rejection, each participant rollback and fresh-context
redecision through real PostgreSQL.

Run the existing HTTP consumer and codec/history checks, root build/style/analyzers/formatting,
direct independent build, all three pending-model checks, local documentation checks and
archive checksums. No new schema is expected. Update the report with actual results and the
complexity removed from commands/stores, without counting only shorter call sites or claiming
historical evidence proves the revision. Leave revision edits unstaged, preserve existing
index entries, and stop at this single capability for implementation review. No commit is
authorized. Owner interface/scope review is complete; implementation review remains.

## Initial owner-reviewed contract and scope — 2026-10-06

The owner approved these documents and asked for inspection of the archive and Marten as
references, with deviations permitted. Approval covers the signatures, behavior below,
new sample rule and exact file dispositions. The later implementation report records new
executions; no runtime guarantee follows from the document review alone.

### Reference inspection and implementation choices

The archived [Inventory store](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/Persistence/StockPositionStore.cs)
checks a write-gate transaction, validates view/header versions, stages stream advancement,
serializes pending facts into positions and invokes its projector. The archived
[aggregate](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/StockPositionAggregate.cs)
evolves and validates a complete candidate before retaining uncommitted facts; historical
evolution does not collect new pending events. The archived
[Purchasing store](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderStore.cs)
uses the same append shape, but treats an empty pending batch as a no-op and classifies
concurrency more broadly. These are inspected historical source, not ES1 test results.

Marten's current [command workflow](https://martendb.io/scenarios/command_handler_workflow.html)
connects aggregate state and observed stream version through FetchForWriting; a competitor
between fetch and save rejects the command. Its current guidance also warns against mutating
the fetched aggregate used as the inline projection baseline. Its
[inline projections](https://martendb.io/events/projections/inline.html) run when saving in
the same database transaction. ES1 retains detached immutable consumer candidates and the
state/version guard, with explicit consumer loading and view staging instead of session-managed
projection execution.

The [append documentation](https://martendb.io/events/appending.html) distinguishes explicit
stream creation from append, and documents append-mode-dependent version behavior. The
[stream table](https://raw.githubusercontent.com/JasperFx/marten/master/src/Marten/Events/Schema/StreamsTable.cs)
and [event table](https://raw.githubusercontent.com/JasperFx/marten/master/src/Marten/Events/Schema/EventsTable.cs)
sources distinguish stream registry identity from event position and optional tenancy/global
sequence. ES1 keeps the existing reviewed EF keys/position constraints, explicit creation at
expected version 0 and checked client-prepared positions. It rejects empty batches, keeps
narrow consumer-native conflict classification, and adds no sequence/progress engine, session,
write gate or runtime dependency. These are design choices, not equivalence to Marten.

The documentation was inspected on 2026-10-06; it labels itself v9.x. Raw source links target
moving master and are comparison evidence, not a pinned compatibility claim. The prior
[reference report](../reports/marten-event-sourcing-reference.md) remains dated evidence.

Implementation retains the module's small early transaction/tracked-header checks so invalid
operation reuse fails before preflight NotFound/Conflict or a domain rejection. The library
independently enforces the same lifecycle for ordinary independent callers. These checks were
not silently removed at the cost of existing behavior. Envelope positions, time/version
validation and header Add/Attach advancement move behind Prepare/Stage as reviewed.

### Inspected callers and extraction finding

The actual append callers are
[StockPositionCommands](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs)
and [PurchaseOrderCommands](../../samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderCommands.cs).
Both currently decide events before loading the header/inline state. Both repeat native
transaction and tracked-header checks, UTC/nonregressing timestamps, checked version
arithmetic, full-batch encoding/materialization, contiguous envelope positions, header
creation/advancement and Attach/AddRange. Purchasing then stages two required views;
Inventory stages one. Their projectors already load untracked version/timestamp-checked
state, so no new reader or projector contract is needed.

The standalone [storage journey](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventSourcing/samples/EventStorageDemo/DemoJourneys.cs) currently
authors finite rows and demonstrates native transactions/mapping, not accepted-batch append.
Its tenant-free rows and customized tables already exercise independent storage adoption.
Its existing counter prefix provides the consumer loading/reconstruction recipe to reuse.

The proposed mechanism owns more than AddRange: one preparation boundary validates the
technical batch, computes/fills positions, verifies mapped identity scope and preserves
the observed header for native concurrency. One staging boundary verifies context/transaction
and tracking before advancing the header. The two modules retain their domain/view
preparation and narrow PostgreSQL error classification. Implementation must report the actual
reduction in duplicated protocol, including new envelope-factory and loading setup. If that
comparison fails to show meaningful leverage, report it before adding further abstractions.

### Proposed public surface and dependencies

Add two concrete types to `ModulithFoundry.EventSourcing.EntityFrameworkCore`, using its
existing row interfaces and EF Relational dependency. No new project, runtime package,
DI registration, options type, service interface or change to existing mapping signatures.

```csharp
public static class EventAppend
{
    public static PreparedEventAppend<TStream, TStoredEvent>
        Prepare<TStream, TStoredEvent, TAcceptedEvent>(
            DbContext database,
            TStream stream,
            long expectedVersion,
            DateTimeOffset recordedAt,
            IReadOnlyList<TAcceptedEvent> acceptedEvents,
            Func<TAcceptedEvent, TStoredEvent> prepareEvent,
            CancellationToken cancellationToken = default)
        where TStream : class, IEventStreamRecord
        where TStoredEvent : class, IStoredEventRecord;
}

public sealed class PreparedEventAppend<TStream, TStoredEvent>
    where TStream : class, IEventStreamRecord
    where TStoredEvent : class, IStoredEventRecord
{
    // No public constructor; only validated preparation produces this value.
    public long ExpectedVersion { get; }
    public long NextVersion { get; }
    public void Stage(CancellationToken cancellationToken = default);
}
```

`TAcceptedEvent` is the caller's type, with no library event base/interface. A caller-supplied
factory serializes one fact and returns a fresh detached concrete envelope with EventId,
EventName, SchemaVersion, Payload and any consumer fields populated. The library fills
StreamId, StreamVersion and RecordedAt. It validates nonempty/distinct GUID event identities,
nonblank names within the existing 200-character mapping, positive schema versions, and a
usable JSON payload; it clones payloads so source JsonDocument disposal cannot invalidate
the prepared batch. It neither decodes payloads nor determines event-family validity.

The header is an untracked existing observation, or a new detached template with Id,
StreamType, ownership/extra fields and Version 0. Creation timestamps/version are filled
only during Stage; existing headers retain their observed original version until Stage.
Preparation captures the header's technical fields and mapped key and validates that it
remains unchanged before staging. Consumer factories must return fresh rows and must not
retain/mutate them or alter the header/context; arbitrary delegate side effects cannot be
rolled back by this utility. Accepted domain objects and input lists remain consumer-owned;
preparation materializes the batch synchronously and does not retain the list for later use.

Supported row models are the ordinary entities/key shape established by
`ConfigureEventSourcingStorage`. Validate native metadata for the header version token and
event/header relationship. Compare actual mapped primary/foreign key values, including any
ownership prefix, before staging; do not add a tenant argument or stamp ownership by naming
convention. Both modules' existing tenant write validators still run at native save.

### Concrete primary consumer journey

Inventory owns a new **stock issue** rule: issue a nonempty batch of positive quantities
in the position's base unit only when their complete checked sum is at most current OnHand.
This is stock leaving this position, not a reservation, catalog update or cross-module sale.
Use the existing one-view loader. Missing/behind/damaged views keep their present failure
behavior; an ahead view remains a conflict. No history replay is introduced into ordinary edits.

Proposed additions to `IStockPositionCommands` are
`Task<StockPositionChangeResult> StageIssuesAsync(IssueStock request, DateTimeOffset recordedAt,
CancellationToken cancellationToken)`, `StockIssue(decimal Quantity)`, and
`IssueStock(Guid Id, long ExpectedVersion, IReadOnlyList<StockIssue> Issues)`.
Add `StockPositionChangeResult.InsufficientStock(decimal Available, decimal Requested)`.
Invalid input throws as today; valid input exceeding available stock returns that domain
rejection with no prepared/tracked batch. Existing Staged/NotFound/Conflict outcomes stay.

The internal fact `StockPositionIssued` carries required positive Quantity and durable alias
`inventory.stock-position.issued`, schema 1. Add an explicit codec registration. Historical
evolution subtracts the recorded quantity; it does not re-run today's availability decision
or authorization. Eligibility and final-candidate checks stay in StockPositionDecisions and
consumer command preparation. Existing opened/received schemas and literal fixtures remain.

Illustrative internal implementation, after request and scope checks (the loading calls
are existing module code; they are not new library interfaces):

```csharp
// The outer caller already started the native InventoryDbContext transaction.
EventStream stream = await LoadOwnedHeaderAsync(request.Id, cancellationToken);
if (stream.Version != request.ExpectedVersion)
    return new StockPositionChangeResult.Conflict();
StockPositionCurrentRow current = await projection.LoadAsync(stream, cancellationToken);
StockPositionState state = current.ReadState(); // View version/time match this header.

// Validate all quantities and sum against loaded state before accepting any facts.
var decision = StockPositionDecisions.Issues(state, request);
if (decision.IsRejected)
    return new StockPositionChangeResult.InsufficientStock(state.OnHand, decision.Requested);
IStockPositionEvent[] accepted = decision.Events;
StockPositionState candidate = StockPositionEvolution.Apply(state, accepted);

var append = EventAppend.Prepare(
    database, stream, request.ExpectedVersion, recordedAt, accepted,
    fact => {
        var encoded = Codec.Serialize(fact);
        return new StoredEvent {
            OrganizationKey = database.RequiredOrganizationKey,
            EventId = Guid.NewGuid(), EventName = encoded.EventName,
            SchemaVersion = encoded.SchemaVersion, Payload = encoded.Payload
        };
    }, cancellationToken);
var next = StockPositionCurrentRow.Prepare(
    database.RequiredOrganizationKey, stream.Id, append.NextVersion, recordedAt, candidate);
var proposed = candidate.ToHistory(stream.Id, append.NextVersion, recordedAt);

// Prepare all required participants first. These calls only stage native tracking.
append.Stage(cancellationToken);
projection.Stage(current, next);
return new StockPositionChangeResult.Staged(proposed);
```

The method-local decision representation above is illustrative consumer code, not a proposed
public library type. The native composition stays visible through the actual module Contract:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
try
{
    var result = await commands.StageIssuesAsync(
        new IssueStock(id, observedVersion, [new StockIssue(4), new StockIssue(3)]),
        recordedAt, token);
    if (result is StockPositionChangeResult.Staged)
    {
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }
    else
        await transaction.RollbackAsync(token);
}
catch
{
    await transaction.RollbackAsync(CancellationToken.None);
    throw; // Dispose context; recover by loading and deciding in a fresh scope.
}
```

Load 13 at version 3, issue 4 and 3: proposed/committed version 5 has OnHand 6. A fresh request
to issue 7 is rejected. Two writers observing 13 cannot both commit their independently
eligible issue batches; native concurrency arbitrates at save. The executable will use a
separate ES1 stream so the existing nine history/inline output lines remain intact.

### Materially different independent adopter

Extend EventStorageDemo with a separate counter journey using the same public Prepare/Stage
calls and existing `journal.streams/facts` model. It references only this Foundry library and
native EF/provider packages. Its state is reconstructed from an explicitly captured header
and ordered prefix, reusing the existing counter JSON loading recipe. Validate the prefix
positions/count and head timestamps locally; no history-library dependency or generic reader
is introduced. Append-only atomic writes permit a bounded captured-head prefix under ordinary
read committed isolation; excluded history is not certified.

Counter eligibility is consumer-owned: a complete batch of positive increases may not take
the loaded value above 25. Creation at value 10 plus accepted increases 7 and 5 produces
version 3/value 22; increase 4 is rejected. Prepare envelopes with direct System.Text.Json,
consumer-generated GUIDs and the existing counter aliases. There is no tenant context,
ownership utility, codec, module Contracts, inline view or projector. Header Description
remains consumer-owned. This changes real obligations: history-derived state versus a required
persisted view, unscoped versus composite ownership, and direct JSON versus the explicit codec.
Both consumers still use native transaction/save/commit and the same append interface.

Keep authored counter/note fixture streams and their literal expectations unchanged. Add a
separate deterministic command-stream identity and a third executable output line. Existing
schema-table assertions still prove two families in one table pair; only total row/stream
counts and output expand to include the additional journey. No migration/model change.

### Supported behavior, errors and ownership

| Case | Proposed contract and owner |
| --- | --- |
| Loading and state/version consistency | Consumer queries its admitted identity/type and verifies state against the observed header. Prepare compares expectedVersion with that same header. It cannot certify arbitrary caller state. Native original-version predicates protect against later competitors. |
| Creation | expectedVersion 0 requires a detached Version-0 header template. Library sets positive next version and equal creation/update times. Consumer checks absence; racing creation resolves through the existing mapped primary key at save. |
| Existing append | Positive expectedVersion must equal the observed header. Positive header version, nonempty Id/type (at most 100 characters), UTC ordered header times and nonregressing append time are required. Equal times remain accepted. |
| Empty batch / arithmetic | Empty batches and negative expectations are argument errors. Checked version arithmetic fails before tracking. There is no header-only advancement. |
| Rejected domain batch | Consumer rejects before Prepare; no header/event/view mutations. Library does not decide eligibility or partially accept a batch. |
| Preparation/factory fault | Factory, codec, payload-copy, envelope/key validation or cancellation faults propagate before header/tracking mutations. Fresh local rows may have been allocated; none is staged. |
| Stage / save / commit | Prepare and Stage do no database I/O or save. Stage is bound to its original DbContext and the same active native EF transaction observed during preparation. Consumer separately stages all required views, saves and commits. |
| Repeated calls | One staged batch per mapped stream key per context, before and after save. Other ownership keys with the same GUID are distinct. A prepared batch cannot Stage twice. No per-context lifecycle registry is introduced; manual Clear/detach and context concurrency are unsupported. |
| Competing writers | Staging is a proposal. EF's header original-version predicate and existing PK/position constraints decide at save. No lock, automatic retry or successful durability result from Stage. |
| Faults / rollback / recovery | After any staging/native persistence fault or rollback, discard the entire context and proposal. Fresh context loads current committed state and makes another explicit decision. No tracker repair, retry/rebase or ambiguous-commit recovery. |
| Cancellation | Caller supplies tokens for loading/preparation/staging/save/commit. Checks before tracking prevent pre-cancelled staging. Cancellation after staging requires consumer rollback/disposal. No claim about in-flight commit cancellation. |
| Ownership / extra fields | Consumer populates all optional ownership/extra fields. Library validates the mapped envelope relationship matches the captured header key; tenant admission and native write validation stay consumer-owned. |
| Additional participants | View preparation/reducers/required cardinality and view Stage calls remain explicit. Native rollback covers them only when the caller includes them in the same module transaction. No generic projector engine. |

Use existing exception types: argument exceptions for malformed input/envelopes,
`InvalidDataException` for invalid observed header integrity, `InvalidOperationException`
for wrong tracking/model/transaction/repeated-use lifecycle, `OverflowException` for checked
version arithmetic and `OperationCanceledException` for cancellation. An expected/observed
version mismatch throws `DbUpdateConcurrencyException` without synthetic EF Entries; modules
retain their preflight Conflict outcomes. Native save exceptions retain real Entries and
PostgreSQL constraint data. Keep InventoryAppendFailures/PurchasingAppendFailures local and
narrow; event-ID collisions and unrelated constraints are faults, not generic conflicts.
Factory/codec exceptions are propagated. No public error enum or translation service.

### Exact proposed file and behavior map

Paths below are relative to the repository root. No deletion/replacement of a demo is proposed.
The existing worktree documentation edits are retained; ES1 additions to shared documents
will be additive and identified separately in the final report.

| Files | Disposition and behavior |
| --- | --- |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventAppend.cs` (new) | Public Prepare entry point; full-batch technical validation/materialization and binding to native context/transaction. |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/PreparedEventAppend.cs` (new) | Reviewed result type; captured header/key, versions, private prepared rows and explicit Stage with native original-version preservation. |
| `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md` | Add explicit preparation/staging usage, supported model and lifecycle/errors; retain mapping recipe. |
| `samples/Wholesale/modules/Inventory/Inventory.Contracts/IStockPositionCommands.cs` | Add IssueStock/StockIssue, StageIssuesAsync and InsufficientStock; retain all existing commands/results. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs` | Adopt Prepare/Stage for opening/receipts and new issues; load matching inline state before issue eligibility; preserve transaction, tenancy, preflight results and explicit required view. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionDecisions.cs` | Add whole-batch state-dependent issue eligibility and checked requested total, with rejection before accepted facts. |
| `samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionEvents.cs`, `StockPositionCodec.cs`, `StockPositionEvolution.cs` | Add issued v1 fact/registration and deterministic subtraction; retain existing schemas and reducer behavior. |
| `samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderCommands.cs` | Adopt the same append interface for drafts/lines; retain detached complete candidate and independently prepared summary, existing loading and error behavior. |
| `samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/StockIssueJourney.cs` (new), `Program.cs` | Executable separate issue stream journey, state-dependent accept/reject and fresh committed read; call after existing journeys. |
| `samples/Wholesale/IntegrationTests/EventPersistence/AppendTests.StateDependent.cs` (new) | Contract-driven issue proofs: independently expected batch/state, rejection, stale/ahead/damaged observations, competing state-dependent writers, rollback and fresh recovery, tenant isolation and live replay. Reuse existing fixture/helpers. |
| `samples/Wholesale/IntegrationTests/EventPersistence/HistoryReadTests.cs` | Extend child-process expected output for the additional journey while retaining existing nine expectations. |
| `src/Gloaming.EventSourcing/samples/EventStorageDemo/CounterCommands.cs`, `CounterAppendJourney.cs` (new), `Program.cs` | Independent bounded counter loading/decision/append and separate explicit transaction journey. Preserve existing DemoData/DemoJourneys. |
| `src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/AppendTests.cs` (new) | Public API and counter proofs on PostgreSQL, including no required views/tenancy, prefix consistency, rejection, competing creation/append, preparation faults, transaction/repeated-call restrictions, rollback and fresh recovery. |
| `src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/StorageTests.cs` | Preserve existing storage assertions; extend executable output/row counts for the added command stream. |
| `samples/Wholesale/modules/README.md`, `samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/README.md`, `src/Gloaming.EventSourcing/samples/EventStorageDemo/README.md` | Record module rule ownership, invocation, independently expected output and technical versus consumer obligations. |
| `docs/plans/es1-bounded-event-append.md` | This concrete proposal, then record owner review and implemented deviations/results. |
| `docs/plans/library-extraction.md`, `docs/design.md`, `docs/development.md`, `README.md` | Add reviewed capability/status/invocations without replacing current checkpoint and T1 notes. No architecture choice is recorded as accepted before review. |
| `docs/reports/es1-bounded-event-append.md` (new) | Slice outcome, implementation leverage, library/template/sample findings, exact new executions, historical evidence and limits. |

Retain unchanged: all mapping helpers/options/row interfaces, consumer native models/contexts,
project dependencies, existing migrations/snapshots, inline loaders/projectors, narrow native
AppendFailures helpers, all literal fixtures, EventCodecDemo and other demos, existing append
tests and assertions, T1 template/tooling/foundation snapshots, archive and checksum manifest.
Existing architecture dependency tests already cover this library and standalone adopter;
rerun them. Both affected test projects already run in the active PostgreSQL CI lane; use
those projects for new cases rather than creating another test infrastructure/project.

### Verification and review gate

After interface review, run both full affected PostgreSQL suites against their actual
PostgreSQL 18.6 Testcontainers/migrations, including all prior E5/E6 faults, view failures,
competing creation/append, later-save rollback, cancellation and console checks. Add direct
public-interface cases for later envelope-factory failure, empty batch, version overflow,
invalid envelope identity/schema/payload, wrong ownership prefix, detached/tracked header,
transaction replacement and repeated Stage. Prove failures leave no staged native changes;
do not replace database collision/participant tests with validation-only assertions.

Run the existing architecture, codec/history suites and HTTP PostgreSQL consumer, full root
build, formatter/style/analyzers, all three existing pending-model checks, documentation links/
whitespace and archive integrity. T1 inputs/composition do not change, so its historical
results remain separate evidence and no unrelated template option is introduced. If selected
foundation/template sources must change, expand the reviewed scope and rerun T1 instead.

Owner review of this public interface and concrete scope is required before adding C#
implementation. That requirement comes from this brief, the extraction/promotion gate and
repository workflow, not a new permission policy. Approval permits implementing and testing
this one capability; it does not permit staging, committing or proceeding into another slice.

## Ownership and supported scope

Domain eligibility, event definitions, reducers, state shape, business identities, Contracts,
tenant admission and failure presentation stay consumer-owned. Historical evolution must not
reapply current command eligibility or authorization rules. Record a new sample rule's owner
and language when that behavior is selected; do not invent business policy in the library.

Use ordinary native EF/PostgreSQL orchestration with caller-owned transactions, saves,
commit/rollback and fresh scopes after faults. A staging method must neither persist nor
publish implicitly. Existing concrete row mappings and optional ownership choices remain
editable. Additional required participants remain explicitly coordinated by the owning
consumer; preserve their existing rollback proofs when adapting its append path.

No automatic event collection, aggregate inheritance requirement, ambient transaction,
assembly discovery, SaveChanges interception, universal provider facade or generic projector
registry is requested. Marten is a reference for coherent supported behavior, not a mandate
to add its runtime or imitate its entire feature set. The native EF direction remains in force.

Projection repair, async progress, snapshots, global ordering, audit/messaging integration,
business-key admission, cross-module transactions, template event presets and production
deployment are separate capabilities. Leave them outside this slice.

## Relevant executable proofs

- Through the actual module Contract, exercise a decision whose eligibility depends on
  loaded state, with independently expected accepted events/state and rejected behavior.
- Prove that state/version observation corresponds to the expected version used for append;
  competing creation and existing-stream writers cannot both win.
- A rejected batch or preparation fault leaves no partially accepted proposal. Caller-owned
  saves and commit determine durability; rollback leaves the previous committed state intact.
- Cover the supported envelope/header faults and required participants affected by the change,
  including recovery using a fresh context. Do not disable or weaken existing assertions.
- Preserve the selected tenant-owned consumer's isolation; exercise the second adopter through
  the same public interface without sample Contracts, Access or unrelated runtime segments.
- Replay remains deterministic and has no external effects. Current decision rules are not
  retroactively imposed on previously recorded facts.
- Run relevant existing tests and checks. Rerun the T1 proof if selected foundation source,
  generation or composition changes affect its output; otherwise preserve its source and
  omission contract without adding an unrelated option matrix.

Do not duplicate EF's internal test matrix. Tests should exercise the new interface and its
consumer obligations, with real PostgreSQL for concurrency/transaction claims. Archive and
previous report results remain historical until those scenarios are actually rerun.

## Handoff and stop condition

Finish with one reviewed interface and implementation, executable primary and independent
adoption, appropriate checks, updated invocation docs and a slice report. Show which caller
complexity disappeared, library mechanism versus consumer policy, template/sample findings,
new versus historical evidence and remaining gaps. Explicitly state if no new reusable
mechanism was proven. Do not silently replace a failed extraction with a broad refactor.

Leave changes unstaged and preserve other worktree edits. No commit is authorized without
owner approval of the exact complete change set. Stop after this capability is reviewable;
do not automatically continue into repair, messaging or sample consolidation.
