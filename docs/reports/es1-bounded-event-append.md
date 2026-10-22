# ES1: aggregate-based bounded event append

Historical design/execution record. The active interface is superseded by
[the owner-approved native EF replacement](../plans/es2-native-ef-simplification.md).
Renamed-source links lead to current replacements; removed mechanisms link to their replacement scope.

The later [provided-store revision](es1-library-write-store.md) adds shared write loading/
validation and explicit required-state native-save integration. Its execution results are
reported separately; counts and local-store descriptions below record the earlier surface.

Date: 2026-10-06. The owner reviewed the replacement interfaces and exact scope before
implementation ("good. make the changes."). This report covers that replacement; its source
is available for line-by-line implementation review. No commit is authorized.

## JSONB follow-up proof — 2026-10-07

The owner requested applying the settled inline-state/query directions. The
[exact revision proposal](../plans/es1-inline-state-enforcement.md) records the new public
signatures and necessary finite-seed adaptations for review. Required-state save enforcement,
appender simplification and the new availability query are not yet implemented or proven.

A new [independent-consumer test](../../src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/AppendTests.cs) uses
the existing reviewed appender to prepare a JsonElement payload from a JsonDocument, disposes
the source before staging/saving, commits, then reloads it through EF in a fresh context. It
checks nested objects/arrays/null, Unicode and escaped text, and a high-precision decimal;
native PostgreSQL reports the payload type as `jsonb`. Expected values are semantic, without
depending on JSONB retaining property order or source formatting. Fresh counter reconstruction
also sees the committed fact. Event payload properties are not used as database filters.

Fresh verification for this follow-up: the independent test project builds without warnings
or errors; all **47** independent-consumer tests passed using disposable PostgreSQL 18.6
Testcontainers through rootless Podman. CSharpier formatted the changed test file. Documentation
links and diff whitespace were checked. This adds one new payload proof and no reusable
mechanism; the other runtime results below remain the earlier ES1 executions. New revisions
are unstaged and the pre-existing index is preserved. No commit is authorized.

## Outcome and adoption

One bounded capability is implemented: consumer-loaded state and its observed version enter
an aggregate, a state-dependent operation accepts a complete candidate, and a configured
native EF appender prepares/stages its pending facts. The caller explicitly saves and commits.
The initial static/delegate EventAppend surface is replaced, not retained as another path.

[Inventory](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionAggregate.cs)
loads the existing required inline state. Issues are eligible only when the complete requested
quantity fits observed OnHand. Its executable opens a separate position, receives 7+6, then
issues 4+3 from 13 at version 3 to reach 6 at version 5. A fresh operation rejects 7. All ten
Wholesale output lines remain asserted, including the nine preceding demonstrations.

[The independent counter](../../src/Gloaming.EventSourcing/samples/EventStorageDemo/CounterAggregate.cs) loads a captured
ordered history prefix through its existing local reader, encodes typed facts directly to
JSON, and has no tenancy, codec/history-library, DI or required-view dependency. Increases
are eligible only when the resulting counter is at most 25. Its separate stream starts at
10, accepts 7+5 to reach 22/version 3, then rejects 4. The original authored counter/note
mapping demonstrations, Description column, shared tables and all three output lines remain.
This varies loading, encoding, ownership and required-participant obligations, beyond names.

Purchasing additionally adopts the same aggregate/appender. Its write-state view and summary
remain explicit participants, and the summary evolves from its own committed amounts.
No new Purchasing issuance behavior is introduced. The archived IssuePurchaseOrderHandler
informed the store journey; its audit participation and tracker-clearing recovery were excluded.

## Mechanisms and consumer policy

| Owner | Implemented responsibility |
| --- | --- |
| Package-free EventSourcing core | Required IEventSourcedAggregate write contract; optional EventSourcedAggregate inheritance. Captures Id/ExpectedVersion, snapshots ordered new facts, checks arithmetic, calculates a complete candidate and validates it once before accepting state/version/pending. Historical initialization collects no pending events and runs no current policy. |
| Native EF event segment | Configured EventAppender, typed EventRecordAdapter and single-use PreparedEventAppend. Generates GUIDs, contiguous positions and one TimeProvider timestamp; validates aggregate/header/count, names/schema/JSON, complete mapped keys and lifecycle; clones payloads and stages the native header/envelopes. |
| Module/local store | Existing inline/history loading and state/version checks; aggregate/observation/transaction association; admitted stream family; required-view preparation/staging. Reload after rejection supersedes the previous untracked observation. |
| Domain consumer | Eligibility, decisions, immutable state/facts, pure whole-batch Evolve and final candidate policy; business Contracts/results and historical meaning. |
| Persistence consumer | Stable aliases/schema/payload encoding, ownership/extra row fields, native models/migrations/provider, tenant admission/save guards and narrow fault presentation. |
| Application caller | Native transaction, final save/commit/rollback, disposal and fresh loading/redecision after failure. Staged results remain proposals until commit. |
| Template | T1 remains its event-free state-stored Catalog/console composition; no event preset or dependency is added. |

ApplyChanges updates accepted aggregate bookkeeping. Evolve is pure candidate calculation.
Existing Inventory/Purchasing reducers stay internal to their modules and are reused by the
aggregate hook and historical reconstruction. Their whole-batch implementation is retained;
Purchasing does not copy its line dictionary once per event. The counter likewise shares its
pure reducer with history loading. Different view shapes retain their own reducers; no generic
projector interface or engine was needed to demonstrate this reuse.

PreparedEventAppend holds concrete prepared encoded rows privately and is bound to its
aggregate/header/context/original transaction. Its public outputs are ExpectedVersion,
NextVersion and RecordedAt. It is a prepared batch, not a durable receipt. Stage checks the
captured aggregate pending references/version and header/key/timestamps, then advances the
native header and adds envelopes. It never saves, commits, stages views or clears pending facts.

JsonElement remains in the unchanged JSON persistence-row interface. Domain facts/state and
business command Contracts contain no JSON. Commands no longer supply RecordedAt. Clocks are
configured once; module registration defaults to TimeProvider.System only when none is supplied.
Finite demos/tests configure scoped deterministic clocks. The adapter supplies encoding and
ownership, while GUIDs/order positions/recorded time are library responsibilities.

The core declares no packages/project/framework dependencies. The EF segment adds only a
reference to that core and retains EF Relational as its sole runtime package. It has no
codec/history/tenancy/actor/Contracts dependency. Independent adoption explicitly references
core plus EF storage and its native Npgsql/design packages. Compiled/declaration checks prove
these boundaries. [ADR 0005](../adr/0005-aggregate-write-contract-and-native-append.md) records
the owner-reviewed placement and required contract, with optional inheritance.

## Complexity removed and retained

The reusable mechanisms are proven through three aggregate families and two materially
different loading/persistence paths. Commands no longer assemble an append expectation,
event array, timestamp or per-call envelope delegate. Named adapters configure family/encoding
once per store; append metadata and lifecycle checks have one implementation. Complete-candidate
acceptance and pending/version bookkeeping likewise have one implementation, exercised through
all three consumers and direct failure tests.

This revision adds source, including concrete stores and aggregates. Shorter commands alone
are not the extraction evidence. Moving existing loaders/view staging into stores is consumer
organization and is not counted as extracted complexity. The evidence is centralized technical
failure obligations, reusable candidate/pending bookkeeping and their executable proofs.
Required-view choice/reduction, domain validation and native completion remain meaningful
consumer work. A generic loading store or projection engine would introduce new policy and
proof costs without evidence from this bounded capability; neither is extracted.

## New proofs for the replacement

[Pure core tests](../../src/Gloaming.EventSourcing/tests/EventSourcingTests/AggregateTests.cs) prove ordered multiple
decisions with a fixed observed version, a read-only pending collection and a snapshot of the
submitted event list. Final-candidate validation occurs once per batch, including a batch
whose intermediate state exceeds policy but final state is valid. A later evolution failure,
final-policy rejection, null event/candidate or version overflow preserves previously accepted
state/version/pending. Historical state above today's ceiling initializes without validation
or pending facts; an empty decision leaves it unchanged. Invalid state/version pairing fails.

[Wholesale Contract tests](../../samples/Wholesale/EventPersistenceDemo.Tests/AppendTests.StateDependent.cs)
prove whole-batch issue rejection and later acceptance, committed inline/live agreement,
competing eligible writers and fresh-context redecision against the winner. Missing/behind/
mistimed views cannot supply decision state; a deterministic commit between header and view
reads produces Conflict. Tenant-scoped histories remain independent. All existing E5/E6
append/history/required-participant checks are freshly exercised through the revised interface:

- Staging and uncommitted saves are invisible to independent readers; commit publishes the
  complete header/event/required-view state. Each failing participant and a second failing
  save roll back the complete native transaction. Fresh operations recover after fault removal.
- Native SQL retains the original header version and ownership predicates. Competing creation
  and append have one complete winner. Repeated staging before/after save requires a fresh
  context; malformed requests and regressing/non-UTC clocks leave no tracked proposal.
- Inline edits reuse their established load paths without event SELECT. Explicit live/temporal
  reads retain captured-head, integrity and historical policy behavior.

[Independent PostgreSQL tests](../../src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/AppendTests.cs) prove
history-dependent eligibility, rejected complete batches, competing creation/append, native
rollback and fresh-context redecision. A deterministic commit between header capture and event
SELECT reconstructs the captured prefix and corresponding proposal, then loses at save; fresh
reading sees the winner. Damaged positions/head timestamps reject decision loading.

The public configured appender is also exercised with a custom aggregate-contract implementation,
proving inheritance is optional. Tests reject mismatched IDs/families, forged versions, empty
batches/null facts, overflow, malformed later metadata, encoder exceptions and reused rows without
staging a partial batch. Payloads survive source JsonDocument disposal. Prepared rows have
distinct nonempty generated GUIDs, contiguous positions and the one sampled batch timestamp.
Changed aggregate version/pending references, header/version/offset or transaction and cancelled
stage fail before tracking. Complete mapped ownership prefixes reject foreign/null keys and
allow the same stream GUID under different owners. Extra Description is preserved.

A historical fact above today's counter ceiling remains repeatedly readable and current
eligibility rejects another increase. Replay does not reapply today's policy or stage facts.
Previous tests submitting empty/duplicate envelope GUIDs are replaced with generated-identity
proofs because the library now assigns those fields. Native identity-collision rollback and
narrow constraint classification remain covered.

EF may insert independent facts before updating the header. A loser can therefore raise the
exact journal stream-position uniqueness constraint before an UPDATE concurrency exception.
Tests require real header concurrency Entries or that exact schema/constraint/SQLSTATE;
event-ID collisions and arbitrary DbUpdateException are not accepted as version conflicts.
The library neither changes native SQL ordering nor translates native faults.

## Fresh verification

All executions below are local results for the replacement, with zero failed/skipped tests:

| Suite | Passed cases |
| --- | ---: |
| Pure aggregate core | 11 |
| Wholesale PostgreSQL history/append/inline/issues | 122 |
| Independent PostgreSQL storage/append/counter | 46 |
| Existing PostgreSQL/HTTP consumer | 97 |
| Architecture/dependencies | 67 |
| Event serialization | 16 |
| Ordered event history | 16 |
| Codec/history compatibility consumer | 16 |
| **Total** | **391** |

Real PostgreSQL 18.6 Testcontainers, actual migrations and rootless Podman were used for
transaction/concurrency claims. Native runner/build/format processes required sandbox IPC
permission. The final timestamp-guard refinement was followed by a full solution build,
both complete PostgreSQL append suites and style/analyzer checks. The other listed suites
were freshly run in this revision; their tested source was unaffected by that refinement.

The 42-project active solution and direct independent core/EF/demo build pass with zero
warnings/errors. Semantic style/analyzers pass. CSharpier checks 359 active files including
generated files. Inventory, Purchasing and independent journal pending-model checks report
no changes. Archive integrity verifies all 800 original files unchanged. Whitespace and local
Markdown link checks pass (886 local links in 82 active documents). These are not observed remote CI or commit-hook results. CI's
active context lane now includes the core tests; existing PostgreSQL lanes retain both adopters.

The initial delegate-interface run passed 373 cases before owner reconsideration. That is
dated evidence for the superseded surface, not proof of this replacement. E6 (`1ae13d4`),
T1 (`8ccf4c8`), archived Purchasing/Inventory source and Marten references informed the design;
their historical execution results do not become new ES1 proofs. T1 generation, foundation/
PersistenceDemo suites, Aspire/browser/broker and archived runtime suites were not freshly
rerun here. No new template or archive runtime mechanism is claimed.

## Review map and remaining limits

Start with [aggregate core](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing/EventSourcedAggregate.cs),
[write contract](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing/IEventSourcedAggregate.cs),
[former configured appender](../plans/es2-native-ef-simplification.md),
[record adapter](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventRecordMapping.cs)
and [former prepared batch](../plans/es2-native-ef-simplification.md).
Then review [Inventory store](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionStore.cs),
[commands](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs),
[Purchasing store](../../samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderStore.cs)
and [independent store](../../src/Gloaming.EventSourcing/samples/EventStorageDemo/CounterStore.cs), alongside their
aggregates/adapters and tests linked above. The [brief](../plans/es1-bounded-event-append.md#exact-replacement-file-and-behavior-map)
contains the exact file/behavior change map, including documentation, clock setup and CI.

Only ordinary registered CLR keys/relationships and the native Version token are supported.
The consumer must supply state corresponding to its observed version; the library cannot
certify domain meaning. Candidate atomicity assumes immutable facts/state and pure evolution/
policy. Arbitrary side effects, mutable retained rows, concurrent contexts/aggregates, tracker
clearing/detaching, tracking-fault recovery, arbitrary model overrides and other providers are
unsupported. Preparation leaves accepted aggregate pending facts as an in-memory proposal;
encoding failure does not undo domain acceptance. Dispose and load again after persistence
failure/rollback. Pending facts are never cleared as an assertion of durability.

Privileged SQL can violate append-only assumptions. Business-key uniqueness, bounded replay
cost, in-flight commit cancellation and ambiguous-commit recovery are unproven. Required
participants are atomic through the consumer's native transaction. Repair, async processing,
snapshots, messaging, audit, cross-module transactions and template event presets remain excluded.

Archive/fixtures/migrations, original demos, T1 tooling/template and unrelated work are
preserved. Revision edits remain unstaged; pre-existing owner-staged entries are unchanged.
No staging, commit, PR or next slice is inferred. Stop at this single reviewable capability.
