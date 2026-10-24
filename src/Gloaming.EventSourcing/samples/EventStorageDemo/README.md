# Native counter append example

One bounded counter uses Gloaming's aggregate/store APIs with native EF/PostgreSQL, ordinary
JSON and a consumer-owned clock/transaction. It has no dependency on Tenancy, Messaging,
ActorIdentity, the optional JSON codec or the Wholesale application.

```bash
export EVENT_STORAGE_DEMO_CONNECTION_STRING='<disposable PostgreSQL connection string>'
dotnet run --project src/Gloaming.EventSourcing/samples/EventStorageDemo/EventStorageDemo.csproj
```

[Program](Program.cs) explicitly migrates the consumer's `journal` schema.
[CounterAppendJourney](CounterAppendJourney.cs) starts one counter, appends two eligible
increases, reads committed history and rejects an increase above its ceiling. Its caller
owns native transaction/save/commit and supplies `TimeProvider.System`. Repeating the command
keeps the same stream and prints `journal append: version=3, value=22, rejected=4`.

[CounterCommands](CounterCommands.cs) makes decisions from the bounded observed history.
[CounterStore](CounterStore.cs) supplies the event envelope mapping and provided store;
[StorageDbContext](StorageDbContext.cs) selects custom stream/event records and table names.
Neither the counter ceiling nor its JSON aliases become library policy.

The [adoption suite](../../tests/StorageAdoptionTests/EventStorageDemo.Tests.csproj) retains
append concurrency, rollback, encoding faults, bounded reads, historic eligibility,
JSON lifetime, alternate ownership prefixes and default-envelope compatibility. Its
[test-owned raw mapping fixture](../../tests/StorageAdoptionTests/Fixtures/DemoJourneys.cs)
also stores independent counter/note families in the same table pair. That older mapping
journey is no longer part of the public executable. Current migrations remain unchanged.

```bash
dotnet test --project src/Gloaming.EventSourcing/tests/StorageAdoptionTests/EventStorageDemo.Tests.csproj
```

The actual counter executable runs twice in a fresh database during the suite. Provider tests
need a container runtime. See the [family contract](../../docs/capabilities.md) and native EF
package for append/rebuild responsibilities and limits; no hosting framework is required.
