# Independent shared event-storage consumer

This small native EF executable uses the package-free EventSourcing core and its native EF adapter.
It has no modules, tenancy, actor, codec registration, DI container or messaging dependency.
The EF adapter now reuses Events.History integrity checks transitively; the consumer's direct
project references remain the EventSourcing core and EF adapter.
Counter/note facts are standalone consumer data, not Wholesale business rules. The authored
mapping fixtures are retained alongside a separate bounded counter command journey.

[CounterHistoryReader](CounterHistoryReader.cs) supplies direct JSON decoding to the provided
bounded EF reader. [CounterStore](CounterStore.cs) delegates ordered prefix loading and validation
to it while retaining historical evolution and its InvalidDataException-facing error policy.

```sh
export EVENT_STORAGE_DEMO_CONNECTION_STRING='<disposable PostgreSQL connection string>'
dotnet run --project src/Gloaming.EventSourcing/samples/EventStorageDemo/EventStorageDemo.csproj
```

[StorageDbContext](StorageDbContext.cs) selects custom journal.streams/journal.facts tables,
maps jsonb through native EF and adds a consumer-defined Description column to its own header
row. [Rows](StorageRows.cs) implement the two library interfaces without a base class or tenant
field. The native design-time factory and migrations remain editable consumer source.

[DemoJourneys](DemoJourneys.cs) explicitly migrates, starts a native transaction, stages the
authored header/envelope rows, saves, commits or rolls back and disposes. Fresh-context queries
select counter/note stream types from the same pair of tables. Expected output:

```text
journal proof.counter: version=2, value=17
journal proof.note: version=1, text=review
journal append: version=3, value=22, rejected=4
```

The same GUID cannot create a second stream under another StreamType: Id is the chosen
identity. StreamType does not participate in the key. Positions are unique within a stream,
events reference an existing header and deleting a referenced header is restricted.

Setup writes only if the store is empty, for an ordinary sequential rerun of this finite demo.
Use a disposable database. Setup provides no concurrent bootstrap or ambiguous-commit recovery.
Payload authoring and the fixture calculation are consumer code. Native mapping alone does
not enforce an append protocol.

[CounterCommands](CounterCommands.cs) adds independently owned counter behavior. It captures
state through [CounterStore](CounterStore.cs), which captures an untracked header and loads
only its complete ordered history prefix, checking positions
and head timestamps before using state. Positive increases are accepted as a complete batch
only when the resulting value is at most 25. Rejected batches leave no tracked changes.
Historical reconstruction never re-runs that ceiling policy. This local reader is neither
a generic history library nor a projector. It has no external effects.

[CounterAppendJourney](CounterAppendJourney.cs) uses a separate stream: start at 10, commit
increases 7 and 5 to value 22/version 3, then reject increase 4. It calls the shared
IEventStore GetForWritingAsync/AppendAsync protocol through the provided EventStore base,
with direct JSON and no tenant,
codec, DI or required inline view. CounterAggregate owns eligibility and delegates pure evolution
to the same reducer as history loading. A configured TimeProvider supplies the batch clock;
the store owns GUIDs and contiguous positions. Domain facts/state contain no JSON.
Every operation explicitly starts its native transaction, saves, commits/rolls back and
disposes. Proposed results become durable only after commit. After faults/rollback, use a
fresh context to load and decide again. Sequential reruns skip completed demo work; they
are not concurrent setup or a production idempotency mechanism.

```sh
dotnet test --project src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/EventStorageDemo.Tests.csproj --no-restore
```

Tests use disposable PostgreSQL 18.6 Testcontainers and actual native migrations. They launch
this executable, verify mixed-type storage/customization, physical identity/relationship
boundaries and a stale native header update. Four focused model cases exercise safe key-shape
rejection and acceptance of differently named ownership prefixes; they require no connection.
The [append suite](../EventStorageDemo.Tests/AppendTests.cs) exercises the supported direct-store contract, state-dependent rejection, captured-head races, competing creation/append,
transaction rollback/fresh recovery, JSON lifetime and scoped-key isolation on PostgreSQL.
The provided-store tests additionally cover version capture without a command expectation,
superseded roots and invalid reconstitution without tracking. JSONB round-trip coverage disposes
source documents and verifies nested/Unicode/decimal payloads in a fresh context and PostgreSQL
column type. Wholesale separately proves required-state save enforcement. See
[the store report](../../../../docs/reports/es1-library-write-store.md).

[Library](../../Gloaming.EventSourcing.EntityFrameworkCore/README.md),
[plan](../../../../docs/plans/e5-3-event-storage-registration.md),
[findings](../../../../docs/reports/e5-3-event-storage-registration.md).
The [ES1 brief](../../../../docs/plans/es1-bounded-event-append.md) records the reviewed append scope.

The library also supplies an optional StoredEventRecord for ordinary envelopes. The
[default-envelope proof](../EventStorageDemo.Tests/AppendTests.DefaultEnvelope.cs) writes two
concrete payload shapes through the provided store with that type, then reads them through
this retained consumer mapping and migrations. Provider JSONB mapping and durable decoding
remain explicit. [Local capability context](../../Gloaming.EventSourcing.EntityFrameworkCore/docs/capabilities.md)
records async/multi-stream/rebuild/provider gaps independently of repository review plans.
