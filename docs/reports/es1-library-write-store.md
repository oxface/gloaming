# ES1 provided write-store follow-up

Historical design/execution record. The active interface is superseded by
[the owner-approved native EF replacement](../plans/es2-native-ef-simplification.md).
Renamed-source links lead to current replacements; removed mechanisms link to their replacement scope.

Status: implemented and verified for owner review, 2026-10-07. The owner endorsed
IEventStore<TAggregate> and authorized concrete implementation. Exact public configuration and
implementation remain available for line-by-line review. Changes are unstaged; the existing
owner-staged index is preserved. No commit, later capability or template preset is authorized.

The later [default-envelope/naming/documentation refinement](es1-envelope-and-library-docs.md)
reruns the store suites with an additional native envelope interoperability proof. Counts and
index hash below record this earlier 413-test execution; source links follow the current
InlineProjectionStorage name.

## Outcome and consumer usage

The library now supplies EventStore<TAggregate,TEvent,TStream,TStoredEvent> behind the
aggregate-only IEventStore<TAggregate>. It concentrates native header lookup, family/UTC
validation, observed/explicit-version comparisons, root/context/transaction association,
required-state loading and event/header/inline staging. Commands perform GetForWritingAsync,
domain Create/operations and AppendAsync; final native SaveChanges and commit remain explicit.
EventAppendResult carries staged version/time and does not assert durability.

StockPositionStore and PurchaseOrderStore now supply ownership plus small main-state bindings;
Purchasing selects a required independent summary reducer once. Their repeated load/version/
transaction/staging protocols were removed rather than hidden unchanged behind an interface.
The main candidate is encoded once; the summary evolves its own committed amounts. State key,
version and recorded time are assigned by the library using the complete trusted stream key.
Domain Create produces the opening event immediately; historical FromState creates no pending
facts and does not run command eligibility.

The independent counter adopts the same write protocol, but retains its captured ordered-history
reader and direct JSON encoding with no tenancy, codec/history library, DI or inline state.
This materially varies state loading, encoding, ownership and required participants. Its three
console lines and Wholesale's ten existing lines remain executable regression assertions.
Inventory's availability query exercises native IQueryable filtering of mapped inline onHand
state, with ownership and SQL filtering before materialization; no event payload query is added.

Explicit model registrations and validation in both module native save overrides require the
changed main/secondary state, advancing header and full contiguous inserted event range in an
active native transaction. Removing a participant after staging no longer permits its events
to save alone through those configured paths. Existing finite history seeds now construct
matching inline main/summary state from unchanged literals and save each module in its own
native transaction. Fixtures, schema, migrations, frozen archive and T1 output are preserved.

## Library mechanism versus consumer policy

| Library mechanism | Consumer-owned policy/setup |
| --- | --- |
| Scoped header lookup; stable family/version/time checks; optional stale-command expectation | Native DbContext/provider and ownership filters; tenant admission and new-stream owner |
| Captured-root and transaction binding; superseded-root and repeated-stage rejection | Fresh operation after failure; conflict/result handling; final save/commit/rollback |
| Existing prepared appender's IDs/order/clock sample/payload cloning/complete keys | Durable registrations/options and event envelope encoding; no library domain fact base |
| Native required-row loading, version/time checks and metadata staging | State shape, decoding/reconstitution, main candidate validation and secondary Evolve |
| Model declarations plus tracked save-boundary participation/range/metadata validation | Which views are required; explicit calls from both save overrides; semantic state-body correctness |
| Aggregate bookkeeping of accepted facts | Domain eligibility, whole-candidate invariants, module Contracts and reducers |

New reusable mechanisms are proven: the provided native write coordinator and explicitly
integrated required-state save validation. This is not an extraction of a history reader,
generic projection engine, transaction/save wrapper or general tenant assignment mechanism.
Module main/summary shapes and availability formula remain sample policy. Native query/filter
naming remains an editable consumer convention. No template changes are made; T1 remains
event-free. The older local append/inline experiments remain reference evidence, not a substitute
for testing this store.

## New executions

All counts below are actual executions against this revision. No skipped or failed tests.

| Verification | Result |
| --- | --- |
| Full active solution, native .NET build | 42 projects; zero warnings/errors |
| Wholesale EventPersistenceDemo.Tests | 141 passed, PostgreSQL 18.6 |
| Independent EventStorageDemo.Tests | 49 passed, PostgreSQL 18.6 |
| Existing HttpIdentityDemo.Tests | 97 passed, PostgreSQL 18.6 |
| ArchitectureTests | 67 passed |
| EventSourcingTests | 11 passed |
| EventSerializationTests | 16 passed |
| EventHistoryTests | 16 passed |
| Wholesale EventCodecDemo.Tests | 16 passed |
| Native semantic style and analyzers | Verification clean |
| CSharpier including generated files | 366 files checked |
| EF pending-model checks | Inventory, Purchasing and independent storage: no changes |
| Archive checksum verification | 800 original files preserved |
| Active documentation/whitespace | 944 local links across 87 Markdown documents; clean |

Total: **413 passing tests**. PostgreSQL suites used disposable Testcontainers through the
rootless Podman socket, native Npgsql/EF mappings and actual retained migrations. The CLI test
and semantic-format commands initially hit sandbox local-IPC restrictions; rerunning with
authorized local socket access succeeded. This was an execution-environment failure, not a
test failure. No remote database or personal credentials were used.

Added store-specific proof cases beyond the earlier append suite:

- Fourteen Wholesale save faults cover detached main state, changed version/time/token,
  missing events/header and omitted Purchasing summary across synchronous/asynchronous saves.
  Saves reject before SQL; rollback leaves independently expected old values, and a fresh
  context accepts a valid batch and commits all participants.
- Two registered-save cases remove the native transaction after staging and verify rejection.
- Two preparation-conflict cases pre-track the committed main row. Append rejects before
  tracking events/header, leaves only that unchanged row, and persisted history/state remains old.
- One native availability query checks threshold semantics, tenant isolation and SQL containing
  the mapped onHand predicate while excluding event-history SELECTs.
- Two independent provided-store cases exercise fetched-version capture without a command
  expectation, superseded roots, repeat rejection and incorrect-version reconstitution without
  tracked mutations, followed by native commit/fresh state inspection.
- The JSONB proof added in the immediately preceding follow-up is rerun here: source-document
  disposal, nested object/array/null, Unicode and precise decimal values survive prepared append
  and fresh-context decoding; native pg_typeof confirms jsonb. This proves the current payload
  boundary, not arbitrary JSON predicate translation or alternate providers.

The retained consumer matrices now exercise the shared store: state-dependent whole-batch
eligibility, rejection without staging, command and inline version consistency, captured-head
races, competing creators/writers, correct native concurrency predicates, participant/save
faults, rollback across saves, historical reconstruction and fresh-context redecision. Console
subprocess smoke tests verify executable adoption and unchanged established output.

Reproduce using [the development guide](../development.md): build the solution, then run its
listed event, HTTP, architecture, aggregate, codec/history and semantic checks. Rootless Podman
prefixes are documented there. Concurrency/transaction assertions require PostgreSQL.

## Review-worthy files and scope

Start with [IEventStore](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/IEventStore.cs),
[EventStore](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventStore.cs),
[state/projection bindings](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateStateMapping.cs),
[native inline state](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/InlineStateReader.cs)
and [model/save validation](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/RequiredInlineStateExtensions.cs).
The previously reviewed EventRecordAdapter, EventAppender and PreparedEventAppend remain the
underlying batch mechanism, with their existing public shapes.

Then review [Inventory binding](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionStore.cs),
[Purchasing binding](../../samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderStore.cs),
[raw counter](../../samples/EventStorageDemo/CounterStore.cs), native model/save integration,
creation/command changes and fixture-seed adaptations. The
[required-state proofs](../../samples/Wholesale/EventPersistenceDemo.Tests/AppendTests.RequiredInlineState.cs)
and [independent proofs](../../samples/EventStorageDemo.Tests/AppendTests.cs) exercise the
supported seam. [The brief](../plans/es1-library-write-store.md) lists exact public types,
errors/dependencies and every file/behavior group beyond the prior ES1 surface.

No source file or fixture is removed; unused module-local staging methods are replaced by the
shared coordinator while their read adapters remain. Unrelated worktree and architecture
follow-up changes from earlier turns are preserved. The index hash remains
`788654fa987b39f24515829171143b112e7903b97b8dc5471887212e360b8bc6`.
Exact complete change-set approval would still be needed before committing.

## Limits and historical evidence

Required-state enforcement is for tracked native saves with the explicit guard installed.
Model annotations alone do not execute it. Raw SQL, bulk updates/deletes, external writers
and bypassed save overrides are outside the contract. The guard checks participation and
metadata/range consistency, not semantic equivalence of manually altered state payloads.
Raw streams remain permitted without inline state. This is not database-level enforcement.

One terminal append per observed stream/context; discard context and aggregate after rollback
or any persistence fault. Manual tracker surgery, transaction replacement with retained proposals,
concurrent context use and mutable/side-effecting adapters are unsupported. Native optimistic
concurrency rejects a loser; no rebase, automatic retry or ambiguous-commit recovery. Missing/
behind required state fails and ahead state conflicts; no automatic repair or snapshot tail.

PostgreSQL is the proved provider. The store performs separate scoped header/state reads and
can detect advancement; it does not promise a snapshot across arbitrary read queries. Complete
stream keys and ordinary one-row-per-stream mappings are supported; one distinct inline row
type per registered family/participant. No multiple command aggregates sharing one stream,
cross-stream order, generated code or deployment/AOT/performance guarantee is introduced.

The archived handlers/stores and Marten are behavioral/source references. The earlier ES1
report's 391-test aggregate-appender run and subsequent standalone 47-test JSONB run are prior
session evidence; neither alone proves this new interface. E6 `1ae13d4`, T1 `8ccf4c8` and the
800-file archive manifest remain historical/checkpoint evidence. T1's full generated-consumer
lane was not rerun for this store-only revision; template sources/output were untouched.

Projection repair, async processing/subscriptions, snapshots, upcasting, messaging, audit,
cross-module transactions and event template presets remain deferred. Their separate contract
and recovery proofs are recorded in [the capability catalog](../plans/event-sourcing-capabilities.md).
Stop here for review of this single capability.
