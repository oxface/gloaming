# Bounded EF event-history reader extraction

Status: owner-approved interface/scope implemented with executable adoption; changes unstaged.
No commit. Existing index entries and unrelated worktree edits were preserved.

## Outcome and complexity removed

The EF adapter now provides IEventHistoryReader<TEvent,TStreamRecord> and the reusable
EventHistoryReader<TEvent,TStreamRecord,TStoredEventRecord> implementation. It loads positions
1 through an observed version, or an explicitly selected earlier positive version, using the
complete mapped stream/event relationship. Keys and the version bound remain SQL parameters;
ordering and ownership predicates run in SQL. Native global filters remain active. The reader
validates the selected range and UTC endpoints before decoding, then returns ordered typed events
with version/time metadata. It does not reload the stream, track, save, repair or acquire locks.

Inventory and Purchasing remove their duplicate ReadRowsAsync/ReadReplayAsync methods and
derive their existing business readers from this implementation. Each retains one DecodeEvent
override using its existing codec, plus native temporal target selection and pure evolution.
Their rebuilders inject these readers directly instead of implementing a history-loading hook.
Ordinary inline commands still require no history reader or maintenance registration.

The independent raw counter adopts the same implementation with ordinary keys, direct JSON and
no tenant/codec/DI registration. CounterHistoryReader retains its creation-event placement/schema
rules; CounterStore retains historical evolution and translates EventHistoryException to its
established InvalidDataException-facing policy. Its direct project references are unchanged.
The standalone ledger consumer also adopts the reader; maintenance remains independently usable.

This proves a new reusable mechanism: native complete-key bounded prefix loading and validation,
not merely an interface replacing a one-line override. The two module readers each lose 37 net
lines, and the counter no longer repeats the native query/metadata loop. The counter's thin decoder
remains deliberately local. No generic aggregate repository or projector engine was introduced.

## Mechanism versus policy

| Library mechanism | Consumer policy |
| --- | --- |
| Mapped complete-key predicate, parameterized ordered SQL and captured-version bounds | Initial stream selection, admitted ownership and native query-filter configuration |
| Selected range/order/time integrity, no tracking/writes and cancellation propagation | Event aliases/schema versions, payload decoding and domain sequencing |
| Typed ordered event metadata | Pure evolution, live/temporal result shape and command eligibility |
| Reader injection into independent rebuilding; existing native concurrency/save validation | Same scoped module context, explicit transaction, authorization, final save/commit and recovery |

The EF adapter adds one explicit dependency on existing Events.History and reuses its integrity
algorithm/errors. It adds no serialization, tenancy, Npgsql, messaging or consumer Contracts
dependency. Events.History remains package-free and independently adoptable. Aggregate core
dependencies are unchanged. Project and assembly architecture rules permit exactly this approved
dependency/error use while retaining exclusions for the other segments and sample types.

## Fresh verification

| Execution | Result |
| --- | --- |
| EventSourcingPostgresTests | **67 passed**, including **21 new reader cases** and **1 new reader/maintenance DI case** |
| Wholesale EventPersistenceDemo.Tests | **155 passed** |
| Independent EventStorageDemo.Tests | **35 passed** |
| ArchitectureTests | **68 passed** |
| EventHistoryTests | **16 passed** |
| **Active relevant total** | **341 passed**, zero failed/skipped |
| T1 external generated consumers | **10 passed** across Cedar and HarborDesk (namespace Task); omission/determinism/parent SDK checks passed |
| Full active solution build | **0 warnings/errors** |
| CSharpier | **400 files checked** |
| Active style/analyzers | Passed; architecture follow-up checked after its final dependency edits |
| Archive source manifest | **800 original files verified** |
| Local documentation targets and whitespace | Passed |

PostgreSQL executions use real **PostgreSQL 18.6** via rootless Podman/Testcontainers. T1 uses
a separate disposable server and exercises independent generated state-stored consumers. Temporary
containers are removed. No substitute in-memory concurrency/transaction claim is made.

New reader cases prove full/earlier ordered prefixes, exclusion of a later committed append and
inclusion after fresh observation, invalid bounds before SQL/decoding, missing first/middle/tail
positions, damaged endpoints/out-of-range/regressing times before decoding, alias/schema/payload/
null decoder faults, cancellation before SQL and between decodes, and wrong stream types.
Composite-key cases use the same stream GUID for two owners and deliberately omit the event-owner
filter: complete-key selection independently isolates histories. A separate event visibility filter
is retained and its hidden middle event fails as an incomplete range. Distinct opening/adjustment
payloads decode to distinct event types. Read cases assert clean tracking and no native writes.

The existing module read suite proves heterogeneous codec dispatch, recorded-time and version
selection, translated predicates/parameters, admitted tenants and a commit between stream capture
and event loading. Append/rebuild suites re-prove competing writers, optimistic repair races,
rollback and fresh-context decisions through the changed reader composition. A deliberately faulty
reader double retains independent rebuilder validation proofs; it is not runtime repair machinery.

Initial consumer checks exposed literal key values in generated SQL. The implementation now uses
EF.Parameter for those values and the existing parameter assertions pass. Initial architecture
checks still denied Events.History; both project and assembly policies now reflect the explicitly
approved dependency. These corrected checks were rerun; failed attempts are not counted as successes.

The earlier 495-test ES2 execution and draft-only compile are historical evidence, not the new
runtime results above. Untouched HTTP/state-persistence suites were not repeated for this reader.

## Review-worthy files and remaining limits

- [Reader interface](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/IEventHistoryReader.cs)
  and [implementation](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventHistoryReader.cs)
  define the reviewed bound, complete-key query, validation and decoding boundary.
- [AggregateRebuilder](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateRebuilder.cs)
  injects the reader without changing state replacement or transaction/concurrency semantics.
- [Inventory reader](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionHistoryReader.cs)
  and [Purchasing reader](../../samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderHistoryReader.cs)
  retain temporal selection and reducers. [Counter decoder](../../src/Gloaming.EventSourcing/samples/EventStorageDemo/CounterHistoryReader.cs)
  demonstrates materially different adoption with direct JSON and ordinary keys.
- [Reader PostgreSQL proofs](../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/EventHistoryReaderTests.cs)
  cover the new mechanism; existing family/consumer tests exercise composition.
- [Consumer contract](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md)
  records setup/errors/limits; [approved scope](../plans/event-history-reader-extraction.md) preserves the proposal.

The selected prefix is materialized in full; capacity, paging and streaming are unproven. Initial
lookup/cutoff selection remain consumer-owned; excluded history is not certified. Arbitrary bypass
writers and in-flight ambiguous outcomes remain unsupported. Only PostgreSQL is verified; no
provider portability claim is added. Catch-up, upcasting, extra/multi-stream/async projections,
maintenance workers and template event presets remain deferred. No migrations, schemas, fixtures,
archive sources or template inputs changed. No new template setup pattern was needed.
