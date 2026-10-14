# Native EF event stores and aggregate rebuilding

The package references the package-free EventSourcing core, Events.History integrity checks,
EF Core Relational and DI abstractions. It does not reference a database provider, tenancy, business Contracts or
an event codec. PostgreSQL is the verified runtime; other relational providers are not certified.

## Model and save boundary

Implement IEventStreamRecord on the header, including its application-managed Guid
ConcurrencyStamp. Use the supplied StoredEventRecord or your own IStoredEventRecord envelope.
ConfigureEventSourcingStorage<TStreamRecord,TStoredEventRecord>() maps headers, envelopes, the complete
PK/FK, unique per-stream positions and native Version/ConcurrencyStamp concurrency tokens.
An overload accepts native composite key expressions for tenant-owned rows. Configure payload
as JSONB in your PostgreSQL consumer. JsonElement is the durable envelope payload, not the
shape of a domain event: heterogeneous concrete event types serialize into separate envelopes.
The library clones payloads before tracking; consumers select aliases, schemas and decoding.

Map one IInlineStateRecord row per complete stream key, its Version as a native concurrency
token, and its RecordedAt. ConfigureRequiredInlineState<TStreamRecord,TStoredEventRecord,TInlineStateRecord>(streamType)
registers the required aggregate. Call ValidateEventStreamChanges() in **both** native
SaveChanges(bool) and SaveChangesAsync(bool,CancellationToken) overrides before calling base.
It validates complete event/state/header participation before SQL. It does not install itself
or protect raw SQL, bulk updates, external writers or consumers which omit the save guard.
DbSet properties and context markers are unnecessary; native Set<T>() and Entry(instance) work.
Use ordinary independent single-table rows with mapped unconverted CLR keys.

## Writing

Derive a typed module store from EventStore<TAggregate,TEvent,TStreamRecord,TStoredEventRecord>.
Its constructor passes the module's concrete DbContext, EventRecordMapping and TimeProvider.
ConfigureInlineState(new AggregateStateMappingImplementation()) binds that aggregate's state.
CreateStream supplies ownership and consumer-specific fields; the library assigns identity,
stream type, event positions, GUIDs, UTC batch time, version and concurrency stamp.
The state mapping converts row to aggregate and aggregate to a fresh detached row. The library
fills its complete key/version/time. It preserves EF's original header and state tokens.

~~~csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
var aggregate = await store.GetForWritingAsync(id, expectedVersion, token);
aggregate ??= PurchaseOrderAggregate.Create(request);
aggregate.SetLines(lines);
await store.AppendAsync(aggregate, token);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
~~~

GetForWritingAsync captures a filtered header and matching inline state. Missing streams return
null; existing required state must exactly match the head and recorded time. No automatic
catch-up or replay happens on this path. Eligibility, reducers, accepted events and business
validation stay in the aggregate/consumer. AppendAsync encodes the entire batch and state before
changing tracked rows, then advances the header and adds envelopes with the updated aggregate.
One append per observation/context is supported. Empty/rejected decisions need no append.
Final SaveChanges, commit/rollback and error classification are consumer-owned.

Raw streams omit required state and override LoadAggregateAsync with their existing captured
history loading path. They can use the provided bounded history reader described below.
This package does not provide a projection engine.
InlineStateReader<TStreamRecord,TInlineStateRecord>.ReadAsync supports filtered state lookup; Validate checks an
already fetched row without another query. Consumers build Queries/Filters with native LINQ,
including mapped predicates and joins. Read queries never repair stored state.

## Bounded event history

EventHistoryReader<TEvent,TStreamRecord,TStoredEventRecord> implements
IEventHistoryReader<TEvent,TStreamRecord>. Derive a module reader and pass its concrete DbContext
and expected stream type to the constructor. Implement DecodeEvent(record) using your event
codec or direct JSON. TEvent may be an interface spanning heterogeneous concrete event payloads;
TStoredEventRecord is their common persistence envelope. No decoder adapter or mandatory codec
package is required. Native scoped DI can alias the concrete reader through the interface.

~~~csharp
var events = await historyReader.ReadAsync(observedStream, cancellationToken: token);
var earlier = await historyReader.ReadAsync(observedStream, throughVersion: 2, cancellationToken: token);
~~~

The caller selects the stream first. ReadAsync never reloads it: the default bound is its
observed version, and an explicit bound must be between 1 and that version. Selection uses the
complete native PK/FK, including ownership components; native query filters remain active.
Ordering and the upper bound execute in SQL. Missing first/middle/tail positions, time regression,
non-UTC metadata and inconsistent stream endpoints fail before any payload is decoded. An earlier
prefix validates its creation endpoint and selected metadata, not excluded history.
An explicit version may fall inside an append batch and reconstruct intermediate state that was
never served as a committed inline aggregate. Recorded timestamps are not unique batch identities.

Results retain each event's version and recorded time. The reader tracks/saves nothing and
requires no transaction for ordinary reads. Missing streams, recorded-time cutoff selection,
tenant admission and live-view evolution remain consumer policy. Rebuilding uses the same scoped
module context and its explicit transaction. A later committed append is excluded from an already
observed prefix; only a fresh stream observation discovers it.

Argument errors and unsupported native mappings propagate as native argument/operation exceptions.
Range gaps/order/time regression use EventHistoryException from Events.History. Invalid stream,
UTC or endpoint metadata uses InvalidDataException. Decoder/EF/provider faults and cancellation
propagate. There is no retry, streaming/page limit, global ordering, catch-up or repair in the
reader. Payload upcasting can be supplied in the consumer's decoder through optional
[Events.Serialization](../../Gloaming.Events/Gloaming.Events.Serialization/README.md);
it adds no dependency or automatic repair to this package.
The entire selected prefix is materialized; no capacity/performance guarantee is made.

## Encoding and EF tracking

AggregateStateMapping explicitly restores a domain aggregate from its persistence record and
encodes its evolved state into a fresh detached record. EventRecordMapping encodes each concrete
event into its envelope. These mappings keep domain construction, payload codecs and consumer
ownership fields explicit. They do not create objects through inferred constructors.

The domain aggregate and tracked EF state record are separate objects. Command decisions evolve
the aggregate first. The library validates all event/state encodings before tracking anything,
then attaches the observed record to preserve its original concurrency values and copies the
encoded values into it. Missing state is added instead. No save or commit happens in this step.
JsonElement.Clone gives an envelope payload an independent JSON document lifetime; it does not
control EF tracking or make JSON mutable. One append per loaded aggregate is supported: pending
facts remain on the aggregate, so a second append through the same observation is rejected even
after SaveChanges. Discard the context and aggregate after rollback or faults.

## Executable examples

- Aggregate writes: [StockPositionCommands](../../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs)
  shows load/create, state-dependent decisions and append; [StockIssueJourney](../../../samples/Wholesale/EventPersistenceDemo/StockIssueJourney.cs)
  supplies explicit transactions, save/commit and rollback.
- Required inline aggregate: [StockPositionStateMapping](../../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionStateMapping.cs)
  converts domain/persistence state; [StockPositionQueries](../../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionQueries.cs)
  filters and joins that stored state with native EF. This is the single required aggregate,
  not an additional persisted read projection. Additional inline views remain deferred.
- Explicit rebuild: [StockPositionRebuilder](../../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionRebuilder.cs)
  delegates history loading and evolution; [RebuildJourney](../../../samples/Wholesale/EventPersistenceDemo/RebuildJourney.cs)
  registers maintenance independently and executes replay, save/commit and a subsequent command.
- Minimal consumer without tenant or codec setup: [Ledger consumer](../tests/EventSourcingPostgresTests/RebuildConsumer.cs)
  and [executable rebuild tests](../tests/EventSourcingPostgresTests/RebuildTests.cs).

Run the complete Wholesale executable using the PostgreSQL connection and command in its
[README](../../../samples/Wholesale/EventPersistenceDemo/README.md). The examples use the same
consumer-owned completion boundary as the snippets above.

## Independent maintenance

Derive AggregateRebuilder<TAggregate,TEvent,TStreamRecord,TInlineStateRecord>, supplying the typed context,
stream type, state mapping and IEventHistoryReader<TEvent,TStreamRecord>. Implement Rehydrate using
effect-free historical evolution, without producing pending events or re-running eligibility.
The rebuilder requests the complete observed prefix from the reader. It retains its input
validation for independently implemented readers. It does not inject IEventStore: that interface
owns writing, and maintenance can be registered without command writing. StockPositionRebuilder
passes its existing module reader directly to the base constructor.

~~~csharp
await using var transaction = await database.Database.BeginTransactionAsync(token);
var result = await rebuilder.RebuildAsync(id, token);
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
~~~

Use a fresh context for one terminal rebuild. It returns null for a missing filtered stream.
It validates prefix length/order/UTC endpoints and reconstructed identity/version/no pending
facts, then inserts missing state or copies encoded values into the observed row. It can repair
an unreadable body without decoding it. Ahead state rejects repair. Healthy state need not be
rewritten. Repair preserves facts, event version and recorded times, changing only the header's
technical stamp. A private exact write association lets the native save guard accept this
state replacement; it is not a public bypass or maintenance scheduling framework.

Every append and repair generates a new stamp. A stale writer can decide from pre-repair state,
but its native header UPDATE loses after repair commits. An append can likewise invalidate an
already captured rebuild. Two repairs compete through the same token. This is optimistic online
maintenance: operations can overlap and fail at save; they do not block before loading. Repair
cannot undo a bad command already committed before capture. Consumers may add operational
exclusion, retries from fresh loads or maintenance windows as their policy requires.

## Registration, errors and recovery

AddEventStore<TAggregate,TStore>() registers writing only. AddAggregateRebuilder<TAggregate,
TRebuilder>() registers maintenance only. AddEventStore<TAggregate,TStore,TRebuilder>() composes
both. Each role is scoped and aliases its concrete implementation through ordinary TryAdd DI
semantics; repeated registration keeps existing bindings. Native overrides remain possible.
The writing helper supplies TimeProvider.System if absent. Separate role constructors choose
explicit typed contexts, so a common scope never resolves an ambiguous bare DbContext. Writing
requires no history registration; maintenance requires no writer registration.

Malformed caller input and envelope metadata use native argument errors. Unsupported models,
missing/replaced transactions, invalid observations, empty or repeated append and clock regression
use InvalidOperationException. Invalid persisted state/history uses InvalidDataException; an
advanced state observed between reads and expected-version mismatch use DbUpdateConcurrencyException.
SQL conflicts use native EF/provider errors, including possible per-stream position/creation
constraint failures before the header UPDATE. Codec errors, overflow and cancellation propagate.
Do not classify unrelated constraints as stream conflicts. Equal UTC recorded times are allowed.

Dispose the whole transaction/context and aggregate after faults or rollback; reload and decide
again. There is no automatic retry, tracker rollback, concurrent context use, ambiguous-commit
recovery or fencing of uncooperative writers. Complete key/tenant admission and final completion
remain consumer-owned. PostgreSQL tests exercise actual native SQL, rollback and fresh recovery.

See [current capabilities and deferred directions](docs/capabilities.md),
[aggregate core](../Gloaming.EventSourcing/README.md),
[standalone raw adoption](../../../samples/EventStorageDemo/README.md),
[tenant-owned adoption](../../../samples/Wholesale/EventPersistenceDemo/README.md),
[reviewed replacement](../../../docs/plans/es2-native-ef-simplification.md) and
[dated ES2 evidence](../../../docs/reports/es2-rebuild-design.md).
