# ES2 single-stream aggregate rebuilding

Status: owner-approved native EF replacement implemented and verified, 2026-10-07.
Complete changes remain available for line-by-line owner review; no commit is authorized.
Checkpoints ab85ec9 and 39c1ab3 remain intact.

## Naming and consumer-reference follow-up

The owner requested clearer generic parameters, helper/field names and explanations of JSON
ownership, mapping and EF tracking. The cleanup uses TStreamRecord/TStoredEventRecord/
TInlineStateRecord; observedStream/streamRecord; streamType; InlineStateMetadata;
EventStreamValidation.ValidateExistingStream; InlineAggregatePersistence/inlineStatePersistence;
PopulateMetadata/AddOrUpdate; and HasAppended. EventBatchEncoder has purpose comments and
blank lines between identity/version checks, time selection, native relationship checks and
individual event encodings. These are naming/readability changes, not new protocols.

The library README now explains detached encoding versus aggregate evolution, preservation of
EF original values, JsonElement.Clone's document-lifetime purpose and the one-append guard.
It links executable aggregate writes, the single required inline aggregate plus native query
filters, and separately registered full replay with explicit native save/commit. Additional
stored projections are deferred; the references do not claim an extra inline projector engine.
ReadEventsAsync continues to delegate to a consumer history reader, keeping maintenance independent
of the write store and avoiding a simultaneous history-reader extraction.

Fresh verification: all **45 EventSourcingPostgresTests passed**, zero failed/skipped, against
real PostgreSQL. Full active solution build passed with **zero warnings/errors**. CSharpier
checked **396 files**; active style/analyzers and whitespace/link checks passed. The prior
495-test and T1 executions above remain historical evidence and were not repeated for this
cosmetic follow-up. No new reusable mechanism was proven. Domain decisions, codecs, ownership,
queries and final completion remain consumer policy; no template, dependency or schema changes.

A public ConcurrencyStamp-to-ConcurrencyToken rename is recorded in the native EF plan for
owner review and is not implemented. The two-argument InlineStateReader remains unchanged in
arity. All new edits remain unstaged; existing index entries are preserved. No commit.

## Native EF replacement, 2026-10-07

The owner approved [the exact replacement scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md)
with “agree, proceed.” Implementation, executable adoption and verification are complete.
New changes remain unstaged; the pre-existing owner/app-staged index is preserved. No commit,
checkpoint reset, archive/fixture change or template-source change was made.

### Result and complexity removed

IEventStore is write-only. Its concrete consumer constructor needs typed DbContext, event
mapping and clock, then configures one aggregate-state mapping. It needs no history reader,
replay adapter, maintenance opt-in or provider gate. AggregateRebuilder is an independent public
base with two consumer integration methods: ReadEventsAsync and effect-free Rehydrate. Shared
native row/key mechanics are internal; the public InlineStateReader only reads/validates.

Removed EventAppender, PreparedEventAppend, PreparedAggregateRebuild, EventStreamReplay,
EventStreamRebuildState, EventStreamWriteGate/EventStreamAccess, ConfigureRebuilding and the
Postgres admission package. Replaced callback/admission/prepared-object lifecycles with direct
whole-batch encoding followed by native EF changes. No retained compatibility aliases, speculative
provider interface, multi-mode projection engine or rebuilding branch in ordinary loading.
The native save guard retains real participation/key/version/time/range checks and one private
exact maintenance-write association, including immutable replacement values. This small record
exists only to reject unauthorized state-only saves; it does not reconstruct aggregates,
coordinate admission, schedule work, retry or commit.

This removes meaningful consumer configuration and public orchestration complexity, not merely
renames methods. The new reusable proof is native optimistic **same-event-version** repair through
a header ConcurrencyStamp changed by both append and rebuild. EF retains captured Version/stamp
original values. Writers and repairs may overlap and lose at save; unlike the former gate they
do not block before loading. [ADR 0008](../adr/0008-native-optimistic-aggregate-rebuilding.md)
records this changed guarantee. No new history-reader/projector/worker mechanism was proven.

### Executable adoption and ownership

| Library mechanisms | Consumer-owned policy and wiring |
| --- | --- |
| Captured header/root association, expected version and one append per observation | Domain decisions, eligibility, immutable evolution and business Contracts |
| Complete event/state encoding before tracker mutation; GUIDs, positions, UTC time and cloned payloads | Durable aliases/schema dispatch, JSONB provider mapping and exact payload decoding |
| Native complete-key mapping, original concurrency tokens and required-state save checks | Tenant admission/value population/global filters, typed contexts and native save overrides |
| Independent full-prefix validation, state reconstruction mapping, exact replacement association and header stamp | Existing history readers, historical reducers, maintenance authorization/windows/retry/scheduling/scaling |
| Separate scoped role registration through ordinary TryAdd semantics | Final explicit native transaction, SaveChanges, commit/rollback and fresh redecision |

Inventory and Purchasing have separate stores/rebuilders and shared state mappings. Their mapped
rows are StockPositionStateRow/PurchaseOrderStateRow. Removed one-method inline projection wrappers;
queries use InlineStateReader or native mapped-state queries. Inventory's actual PostgreSQL
availability filter and complete-key header join remain proven without event reads. Purchasing
summary is derived from main state. This proves the sample's query boundary, not universal
multi-aggregate or cross-module query/projection behavior.

Removed dormant PurchaseOrderSummaryRow/mapping/seed writes and its three legacy-row-only tests.
Added separate header-stamp migrations for all three consumers and a separate legacy-summary
removal migration. Prior migration source and literal fact payloads remain unchanged. Added stamp
columns initialize existing headers through PostgreSQL gen_random_uuid(); library writes supply
their own stamp. No production upgrade/cutover compatibility or concurrent schema migration claim.
Commands now use Open/Receive/Issue/Draft/ChangeLines/Rebuild and Changed result variants; fixture
seeding uses synchronous Add. Changed still requires explicit save/commit for durability.

The tenant-free direct-JSON ledger proves independent aggregate/store/rebuilder adoption without
Wholesale, Events.Serialization, tenancy or another library family. The independent raw counter
retains its history-backed loading path and no required state. Default StoredEventRecord supports
heterogeneous concrete payload shapes through the supported store. T1 stays event-free.

### New executions against the replacement

All tests below completed with **zero failures and zero skips**. PostgreSQL suites used real
PostgreSQL 18.6 on rootless Podman, EF 10.0.12 and Npgsql EF 10.0.3; no in-memory concurrency claims.

| Suite | Passed |
| --- | ---: |
| Family EventSourcingPostgresTests | 45 |
| Wholesale EventPersistenceDemo.Tests | 155 |
| Independent EventStorageDemo.Tests | 35 |
| ArchitectureTests | 68 |
| Package-free EventSourcingTests | 11 |
| EventHistoryTests | 16 |
| EventSerializationTests | 16 |
| EventCodecDemo.Tests | 16 |
| Existing state-stored PersistenceDemo.Tests | 36 |
| Existing HttpIdentityDemo.Tests | 97 |
| **Active total** | **495** |
| T1 Cedar + HarborDesk external generated consumers | **10** |

Family concurrency tests capture corrupt same-version state, allow repair to commit, observe a
native header concurrency failure for the old writer, roll back, then reject the old decision
from a fresh restored aggregate. Actual generated UPDATE SQL includes Version and ConcurrencyStamp
predicates. The reverse race lets append win after repair capture, rejects the old replacement,
then full replay in a fresh context uses the new head. Two repairs and two writers each have one
native winner. Saved repair, missing-state insertion and a second save remain rollback-safe.
SQL CHECK failure and cancellation during executing replacement preserve old state until fresh
repair succeeds. Full replay rejects gaps/order/duplicate/time/schema/endpoints, ahead rows,
wrong reconstructed identity/version and pending command facts before tracking.

Native maintenance save tests reject detached state, changed version/time/UTC offset/body,
changed original tokens, header/history mutation and omitted required transaction/fresh context.
Both native save paths are exercised. Wholesale retains state-dependent whole-batch rejection,
competing creation/appends, tenant and complete-key isolation, strict missing/behind/corrupt/ahead
loads, event/state/header SQL failures, rollback across two saves, query translation and executable
module maintenance recovery. Raw tests now use the actual store instead of prepared-handle
probes; JSONB survives source-document disposal with Unicode/nested/decimal fidelity in a fresh
context. Tests tied only to removed lifecycle/gate/custom DI protocols were replaced or removed,
not retained under misleading blocking names.

Additional fresh verification: all **43** solution projects build with zero warnings/errors;
CSharpier checks **396** files; semantic style/analyzers pass; all three native migration models
have no pending changes; archive manifest verifies **800** original files; active documentation
links and git diff whitespace checks pass. T1's unchanged TypeScript verifier exercises deterministic
creation, omission, conflicts/publication races and both generated PostgreSQL consumers outside
the checkout (including namespace Task). Disposable containers are removed after verification.

Local logs: /tmp/es2-final-family-tests.log, /tmp/es2-final-wholesale-tests.log,
/tmp/es2-raw-tests.log, /tmp/es2-final-existing-checks.log and individual suite logs,
/tmp/es2-final-build.log, /tmp/es2-final-format-check.log, /tmp/es2-final-style.log,
/tmp/es2-final-analyzers.log and /tmp/es2-final-t1-proof.log. Logs are temporary evidence;
durable regression coverage lives in the family and executable consumer test projects.

### Review-worthy files and remaining limits

Review [EventStore](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventStore.cs),
[batch encoding](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventBatchEncoder.cs),
[AggregateRebuilder](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateRebuilder.cs),
[InlineStateWriter](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/InlineStateWriter.cs),
[native save guard](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/RequiredInlineStateExtensions.cs),
[registration](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventStoreServiceCollectionExtensions.cs),
[actual stamp races](../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/ConcurrencyTests.cs)
and the separate module bindings/migrations in the reviewed map. Public mapping/reader/header
contracts are explicit and self-sufficient in [the package README](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md).

No automatic retry/rebase, catch-up, snapshot, async/multi-stream or secondary persisted projection,
job table, upcaster, messaging/audit integration, cross-module transaction or template event preset.
The maintenance worker remains planned and needed eventually, deferred to focus this change.
Other relational providers are unproven; future actual pessimistic needs may earn a separate
provider package. Consumers must install both native save guards and own the explicit transaction.
Raw SQL/external writers, arbitrary adapter side effects, concurrent DbContext use and ambiguous
commit recovery are outside this contract. Repair cannot undo already committed bad commands.

The retained TypeScript es2-coordination diagnostic and the earlier gate/multi-view reports below
are **historical design evidence**, not the current native regression suite. Their rejection of
unchanged-version optimistic predicates does not reject this newly proven changing stamp. No
Aspire runtime, library worker or additional reusable projection mechanism is claimed here.

## Aggregate-only reduction, 2026-10-07

Historical implementation, superseded by the native EF replacement above.

The owner approved [the exact replacement scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-aggregate-only-reduction.md).
That implementation maintained one aggregate state per registered stream, with strict ordinary
loading and a separate explicit full-rebuild lane. Secondary-view orchestration is removed.
That implementation's Purchasing summaries derived from checked aggregate state; legacy summary mappings,
tables, migrations, seed rows and literal fixtures remain intact. Commands no longer maintain
those rows and current queries ignore them. Raw counter adoption remains independent.

This refinement proves **no new reusable mechanism**. It narrows the existing append/rebuild
mechanisms and adds consumer proofs for the reviewed replacement. Earlier multi-view outcomes
and executions below are historical evidence, not current guarantees. Removing complexity did
not require replacing reducers, schemas or migrations with a new abstraction.

### Mechanisms retained and complexity removed

EventStore now captures one state observation and prepares one append candidate. It forwards
IAggregateRebuilder to a dedicated internal AggregateRebuilder. PreparedAggregateRebuild owns
repair-only staging and exact native-save validation; ordinary PreparedInlineState has no
rebuilding flag or repair fields. Complete event encoding still finishes before tracking.
ConfigureRequiredProjection, InlineEventProjection, isMainState and ProjectionCount are removed.
The maintenance role/result are now IAggregateRebuilder and AggregateRebuildResult(Version,
RecordedAt). Typed-context selection and shared scoped DI role identity remain explicit.

Against the pre-reduction ES2 index, EventStore shrank **530 → 272 lines**. Across EventStore,
InlineProjectionStorage, EventStreamRebuildState, EventStreamReplay, RequiredInlineStateExtensions,
InlineAggregateAdapter and the old/new maintenance interface and new implementation files,
the total is **1,414 → 1,333 lines**, including the moved implementation and shared bindings.
That is a modest **81-line reduction**, not a claim that moving methods eliminated their
obligations. The meaningful simplification is the removed secondary-row collection/configuration
and one-state operation shape. Admission, native key mapping, prefix checks and save association
remain because their omission has demonstrated concurrency/integrity failures.

The library owns technical append metadata, observation/version comparison, native transaction
association, registered state participation, replay metadata validation and cooperative writer/
repair coordination. Consumers own domain eligibility and reducers, codec/schema selection,
tenancy/authorization, Queries/Filters, mappings/migrations and final save/commit. The optional
provider owns the PostgreSQL algorithm. There is no new dependency or template setup; T1 stays
event-free. Live reconstruction reuses existing consumer history readers, with no shared reader
or projector engine and no implicit catch-up.

### New executions for this reduction

PostgreSQL **18.6**, .NET SDK **10.0.112**, EF **10.0.12**, Npgsql EF **10.0.3**.
No skipped cases in the executed suites. All counts below are fresh reduction executions;
the broader initial and registration-refinement tables later in this report remain separate.

| Executed checks | Result |
| --- | --- |
| Family standalone provider/registration suite | **58 passed**; includes new rejection of two different state types for one family without altering the first registration |
| Wholesale event persistence suite | **156 passed**; includes three legacy-summary omission/behind/corruption replacement proofs |
| Independent raw counter / architecture / aggregate core | **50 / 69 / 11 passed** |
| Existing state-stored persistence / HTTP consumer suites | **36 / 97 passed** |
| Optional Events serialization / history suites | **16 / 16 passed** |
| Total across these nine active suites | **509 passed**, no skipped cases |
| Fresh T1 external consumers | **10 passed**, five per generated consumer; deterministic creation, namespace Task, conflicting parent SDK pin, local references and event/messaging omission passed |
| Full active solution | **44 projects**, **0 warnings/errors** |
| CSharpier / active style and analyzers | Passed; **389 files** checked by CSharpier |
| Inventory / Purchasing native pending-model checks | **No pending changes** |
| Frozen archive | **800 original files preserved** |
| Active Markdown file targets / git whitespace | **1,117 local file links in 109 documents**, checked; passed |

Real PostgreSQL proofs retain state-dependent command eligibility/rejection, contiguous
heterogeneous batches, state/head equivalence, competing writers, shared/exclusive coordination,
full-prefix integrity, native sync/async guard errors, SQL failures, cancellation, rollback and
fresh-context recovery. SQL-failed/cancelled rebuild replacement now targets the one aggregate
state; there is no fictitious secondary-participant guarantee. The executing-update cancellation
proof still observes PgSleep through pg_stat_activity, and admission proofs observe pg_locks.
Wholesale proves main-state repair restores derived summaries while damaged legacy summaries
remain irrelevant. Three new cases compare legacy rows before/after ordinary commands and
assert current summaries despite missing, behind and malformed legacy data. Six secondary-only
case instances were removed/replaced; the count change 159 → 156 is deliberate, not a lost
claim of current coverage. The executable history/command/rebuild journeys still pass.

The new registration proof initially omitted recorded-time mapping in its convention-free
ModelBuilder setup; explicit mapping corrected the setup before the final passing run.
Sandboxed Roslyn/MSBuild IPC invocations initially failed; the same authorized build/style
checks passed with local IPC enabled. These were environment failures, not ignored checks.
The diagnostic TypeScript script and its earlier four SQL executions were retained, not rerun
as new evidence here. It reproduces rejected locking alternatives independently of the family
integration tests; it is not a production worker. No new Aspire/browser/broker/archive runtime
execution is claimed. Archived source integrity was freshly checked instead.

### Review and remaining gaps

Review [EventStore](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventStore.cs),
[AggregateRebuilder](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateRebuilder.cs),
[former PreparedAggregateRebuild](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md)
and [the native save guard](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/RequiredInlineStateExtensions.cs)
with [the exact reduction map](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-aggregate-only-reduction.md#exact-filebehavior-map).
Purchasing's query/store/mapping and replacement tests show the changed consumer behavior.
Current setup, capabilities, limits and future context are recorded in family/EF/provider-local
documents; the ES2 brief and ADRs reflect the narrower supported contract.

A library maintenance worker is **still planned and needed eventually**, deferred to simplify
this change. Consumers can build reconciliation around the explicit operation and own
scheduling, scaling and online/offline orchestration. The selected library gate still defines
supported online safety; an application maintenance window does not turn ungated writers into
a proven online protocol. No worker/job table, general lease manager or offline-mode API.
Catch-up remains deferred under the always-current inline contract. Historical schema reading/
upcasting remains a priority; broader inline/multi-stream projections need an actual view after
native EF join/filter assessment. Current single-ID summary derivation does not prove server-side
filtering/paging on calculated totals or cross-module querying. Other provider, resumability,
external-writer, semantic body tampering and ambiguous-commit limits remain explicit.

Migrations, literal fixtures, frozen archive and T1 inputs/checkpoints are preserved. Existing
index entries were retained; these reduction edits remain unstaged. No commit was made.
Stop here for complete owner review of this one bounded capability.

## Historical multi-view outcome and extraction finding

One explicit maintenance call rebuilds an existing stream's main inline state and every
registered required secondary view from retained full history, at its captured version/time.
It repairs missing, behind and unreadable/wrong old bodies without restoring those bodies.
Events/header metadata are preserved. Fresh state-dependent writes resume after caller commit.

A **new reusable mechanism was proven**: pre-read transaction admission plus validated replay,
complete candidate preparation and an exact prepared projection-only native-save contract.
This is meaningful coordination beyond a convenience method. Replacing it with consumer code
would repeat writer/repair fencing, captured-prefix validation, context/transaction/key lifecycle,
observed row-token staging and complete-participant save enforcement in both modules. Module
maintenance entry points now establish admitted tenancy and invoke one operation; they do not
reimplement those obligations. The explicit replay adapters remain small consumer bindings.

Inventory rebuilds one JSON state. Purchasing rebuilds JSON main state and an independently
evolved scalar/JSON summary; Evolve(null, allFacts) never depends on the main body. The standalone
provider consumer uses direct JSON and the library envelope without Wholesale, tenancy or optional
Events utilities. The history-only counter remains an independent ungated/raw adopter.

## Mechanism versus consumer policy

| Library mechanism | Consumer-owned policy/setup |
| --- | --- |
| Shared admission before write loading; exclusive admission before repair head capture | Provider selection, trusted complete identity, native typed context/transaction and all supported writers following the protocol |
| Captured replay position/UTC/endpoints and reconstructed identity/version/pending checks | Event schema registry, decoding, historical semantics and effect-free reconstruction |
| Prepare every fresh candidate before tracking; complete-key metadata and observed original version staging | Main-state encoding, independent secondary evolution and native mappings/migrations |
| Private exact header/row/context/transaction repair association and both-save-path validation | Install native save overrides; tenant validation, maintenance authorization, final save/commit and fresh retry |
| Parameterized PostgreSQL transaction advisory locks in optional provider package | Database permissions, timeout settings, external writer discipline and operational deployment |

Core remains package-free. EF references core, EF Relational and DI abstractions for optional
aggregate registration. The new optional EventSourcing.Postgres project references EF and the
existing pinned Npgsql EF package.
IEventStore<TAggregate> stays unchanged; IInlineProjectionRebuilder<TAggregate> is the separate
maintenance interface. There is no public bypass/repair permit, automatic save/commit,
aggregate discovery, generic history query engine or projector engine.

## Historical initial executable evidence

Final executions on PostgreSQL **18.6**, .NET SDK **10.0.112**, EF **10.0.12** and Npgsql EF
**10.0.3**. No skipped tests in the executed suites. Intermediate failure: the native executable
output assertion lacked the two added maintenance lines; corrected in the already-scoped
HistoryReadTests file, preserving all prior demo output expectations.

| Executed checks | Result |
| --- | --- |
| New standalone EventSourcingPostgresTests | **47 passed** |
| Wholesale EventPersistenceDemo.Tests | **159 passed**, including **18 new rebuild cases** and all 141 retained cases |
| ArchitectureTests | **69 passed**, including 2 new provider/adoption direction cases |
| Remaining nine family suites | **162 passed** |
| Context/codec sample suites | **31 passed** |
| Existing PersistenceDemo / independent EventStorageDemo / native HTTP suites | **36 / 50 / 97 passed** |
| Total across 17 executed active projects | **651 passed** |
| Fresh external T1 generated consumers | **10 passed** (5 each); deterministic creation, conflicting parent SDK, namespace Task, local-source references and event/messaging omission passed |
| Full active solution | **44 projects**, build passed with **0 warnings/errors** |
| CSharpier; active style/analyzers; git whitespace | Passed; CSharpier checked **385 files** |
| Native pending-model checks | Access, Inventory, Purchasing, Sales and independent EventStorageDemo: **no pending changes** |
| Frozen archive integrity | **800 original files preserved** |
| Local Markdown links/new-file whitespace | **1,095 local links in 108 active documents**, passed |

Provider proofs observe actual blocked **pg_locks** advisory entries rather than infer blocking
from timing. An earlier already-loaded writer commits before repair captures its new head;
a later writer cannot load until repair commits and then rejects an unaffordable decision using
corrected state. Shared writers still overlap and produce one native optimistic winner. Native
reads, another key, another schema and another admitted owner continue under the per-key gate.

Other new proofs cover two rebuilders, cancelled lock wait, SQL participant failure, saved repair
rollback, fresh recovery and cancellation during an executing update observed through
**pg_stat_activity/PgSleep**. Both serving rows survive cancellation/failure and fresh repair
can reacquire. Corrupt prefixes/order/duplicates/times/endpoints/schema, invalid domain sequence,
wrong reconstructed identity/version and ahead repair state fail before replacement tracking.
Missing/foreign streams, dirty/mixed/reused contexts, missing/replaced transactions, snapshot
isolation, missing binding, foreign gate context and unsupported provider/schema/converted/key
types exercise early errors. Native save tests reject direct projection mutation, omission,
version/time/original-token tampering and header/event mutation during prepared repair. Native
append shapes and the lower-level appender require shared admission for enabled registrations.

Fresh module queries and retained replay agree after repair; Inventory's next issue eligibility
uses repaired availability. Purchasing summary totals are checked independently. A SQL failure
in its summary preserves its unreadable prior main body and old total, then fresh repair restores
both. Existing multiple-write-key/save regression tests remain green. Literal history seeding
now acquires shared admission asynchronously before its retained synchronous Stage construction;
fixture bytes, historical positions/times and migration files are unchanged.

## Historical evidence and rejected alternatives

[The earlier design report](es2-rebuild-design.md) records archive inspection and four native
SQL diagnostics, with a reproducible [script](../../src/Gloaming.EventSourcing/docs/proofs/es2-coordination.ts).
Those executions preceded implementation and are distinct from the new EF/provider/consumer
proofs above. Repair-only SELECT FOR UPDATE on the header was rejected: a previously loaded
writer still overwrote repaired state because repair deliberately preserved its version.

Archived Inventory Organization-wide gating, permission/audit orchestration and old repair tests
are historical reference evidence; those tests were not rerun as ES2 proofs. Marten supplied
behavioral references; no Marten dependency or code-generation runtime was introduced.
[ADR 0007](../adr/0007-pre-read-admission-for-inline-rebuilding.md) records the reviewed pre-read,
per-key, version-preserving direction. That additional documentation file was presented before
writing, as AGENTS.md requires recording settled architectural choices; no new interface resulted.

## Review map and preserved scope

The [reviewed brief](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-aggregate-only-reduction.md#exact-filebehavior-map)
lists the complete file/behavior map. Start line-by-line review with:

- [EventStore](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventStore.cs): configuration, pre-read shared admission, full replay and all-projection preparation/staging.
- [former EventStreamWriteGate](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md) and [former private state](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md): complete key, transaction/access lifecycle and exact prepared repair association.
- [RequiredInlineStateExtensions](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/RequiredInlineStateExtensions.cs), [InlineProjectionStorage](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/InlineStateReader.cs), EventAppender and PreparedEventAppend in the same project: registered aggregate rebuilding, native guards, raw repair loading, observed-token staging and lower-level admission enforcement.
- [former Replay surface](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md), [maintenance interface/result](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/IAggregateRebuilder.cs) and [former provider implementation](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md).
- [Aggregate DI registration](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventStoreServiceCollectionExtensions.cs) and [registration proofs](../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/RegistrationTests.cs): same scoped roles, clock overrides, incompatible binding rejection, typed context isolation and explicit declaration compatibility.
- [Standalone proofs](../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/RebuildTests.cs), [real consumer proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/AppendTests.Rebuilding.cs) and [executable maintenance journey](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/RebuildJourney.cs).

Modules select the provider, alias one concrete scoped store to both interfaces and reuse the
same concrete history reader for existing reads and captured replay. Command-only composition
supplies that reader; query-only composition acquires no gate. Maintenance Contracts expose only
consumer NotFound/Staged metadata. Both existing DbContext save overrides remain unchanged.
CI, hooks, solution and development commands register the provider proof lane. Existing shared
.editorconfig rules already apply; no edit was necessary. Library/family-local documentation
is self-sufficient about setup, current guarantees, errors and deferred context.

Archive, literal fixtures, migrations, Directory.Packages.props, aggregate rules, ordinary
Queries/Filters, template/creator inputs and unrelated families are unchanged. T1's selected
source payload remains event-free; the new provider is not silently exported. Existing demos
are preserved with one additional journey. No staged entries or commits were created.

## Limits and remaining work

Rebuilding is an explicit existing-ID operation in a fresh untracked context and caller-owned
ReadCommitted transaction. It restores every declared view and preserves head/time; it does not
compare prior bodies, discover streams, infer a new aggregate from a missing view, or repair
business decisions already committed from corrupt state. Full replay materializes O(events +
state), with no fixed quota/resume/performance guarantee.

Cooperative admission excludes only supported participating writers. Raw SQL, bulk/external
writers, old ungated binaries, bypassed overrides and arbitrary in-process body tampering are
outside enforcement. Database/key equality must match the canonical identity supplied by the
consumer; custom string collations/equality and rolling key-format/schema migrations need
separate proof/coordination. Hash collisions may add contention. No generalized deadlock retry,
production privilege/operational claim or other-provider certification was tested.

Async projections/global progress, snapshots/tails, upcasting, multi-stream grouping/batches,
discovery/jobs/admin UI, shadow revisions/cutover, messaging, audit, RLS and cross-module
transactions remain deferred in the [library-local catalog](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/docs/capabilities.md).
Aspire/browser, archived backend and broker suites were not rerun; this slice changes no such
runtime/transport capability. Stop here for complete unstaged owner review.

## Owner-review follow-up, 2026-10-07

The retained manual diagnostic was translated from Python to TypeScript at the owner's
request. Its four native SQL cases passed again on PostgreSQL 18.6; strict TypeScript checking
and Prettier passed. The original Python implementation is replaced, and reproduction links
now use Node 24.21+. These results do not add C# integration proofs or rerun the suites above.
The repository workflow now records TypeScript as the preferred durable scripting language.

[The reviewed refinement](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/library-extraction.md#es2-owner-review-follow-up) now supplies
AddEventStore<TAggregate,TStore> for one scoped concrete store and both role aliases, preserving
clock overrides and rejecting incompatible registrations before partial changes. Main inline
registration now includes rebuilding, removing a redundant model call from both modules and
the independent ledger. The explicit helper remains idempotent for earlier setup. A missing
gate/replay rejects registered creation/loading before staging. Raw history-only adoption remains.

This is a setup simplification of the existing repair mechanism, not an additional repair
algorithm. Safe online repair retains pre-read writer/exclusive admission, full captured-prefix
validation, all-required-view preparation and exact native-save participation. Those obligations
have demonstrated failures when omitted. No jobs, discovery, snapshots, automatic repair or
general projector engine was added. Optional module facades preserve internal aggregate/public
Contract separation; direct generic rebuilder use needs no facade. Historical schema/upcasting
remains a priority for a future proposal and is not implemented.

Fresh refinement verification on the same database/SDK/provider configuration:

| Executed checks | Result |
| --- | --- |
| Standalone provider/registration suite | **57 passed**, including **10 new** registration/default cases and all 47 earlier cases |
| Wholesale / raw counter / architecture / persistence consumer suites | **159 / 50 / 69 / 36 passed** |
| Total in these five suites | **371 passed**, no skipped cases; the full earlier 651-test execution remains separately dated evidence |
| Full active solution | **44 projects**, build passed with **0 warnings/errors** |
| CSharpier / active style and analyzers | Passed; **387 files** checked by CSharpier |
| Inventory / Purchasing native pending-model checks | **No pending changes** |
| Fresh external T1 generated consumers | **10 passed** (5 each); deterministic creation, namespace Task, conflicting parent SDK, selected local references and event/messaging omission passed |
| Frozen archive / Markdown links and whitespace | **800 original files preserved**; **1,102 local links in 108 documents**, passed |

The refinement adds two C# files (registration helper/tests), the explicit DI abstractions pin
and package reference, updates model/registration declarations, guard and dependency checks,
and updates local/root documentation. Reducers, history readers, module maintenance Contracts,
seed construction, fixtures, migrations, frozen archive and T1 source payload are preserved.
Existing staged entries were preserved as found; these follow-up edits were left unstaged.
No commit was made. The earlier broader 17-project suite execution is separate evidence;
T1 was freshly rerun for this refinement against an owned disposable PostgreSQL container.

## Readability review and fresh design assessment, 2026-10-07

The owner requested clearer names/comments and reconsideration of the entire shape, including
active schema/sample replacement without backward compatibility. Internal cleanup is implemented;
[new public/schema replacement](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md) awaits owner interface review.
Current admission/append/rebuild guarantees remain unchanged. No new reusable mechanism is proven.

AggregateRebuilder now separates admission, captured-prefix validation, historical evolution,
raw old-row observation and native replacement with blank lines and purpose comments. Internal
names distinguish streamHeader, originalHeaderValues, persistedStateRow, replacementStateRow,
stateKeyValues, originalStateVersion and expectedEntryState. Other affected files use
streamForeignKey/eventStreamForeignKey, streamPrimaryKey/eventPrimaryKey, observedStreamKeyValues
and explicit admission bookkeeping names. PreparedEventAppend now uses consistent camelCase
private fields. Public type/method signatures remain unchanged. The foreign-key relationship
maps dependent state/envelope properties to the stream's primary key; it is not just key values.

Removed two duplicate checks without dropping their outcome: InlineProjectionStorage.LoadAsync
lets Validate perform the behind-version check once; PreparedAggregateRebuild already compares
the exact captured entry state, making another Added/Modified check redundant. Tests verify the
same missing/behind/ahead, transaction, native guard and recovery behavior.

Fresh cleanup verification: **58 provider + 156 Wholesale + 50 raw counter = 264 passed**,
zero failed/skipped, on real PostgreSQL 18.6. Full active build passed, **0 warnings/errors**;
CSharpier checked **389 files**, and style/analyzers passed. This is a narrower execution than
the previous nine-suite/T1 reduction proof; those results remain separately dated evidence.
No model, migration, fixture, dependency or template input changed in this cleanup.

The assessment identifies maintenance coupling as the primary complexity, rather than simply
renaming classes. A temporary TypeScript/native SQL diagnostic exercised a header concurrency
stamp: repair invalidating an old writer (including rollback of earlier state/event SQL), append
invalidating a captured repair, and competing same-version repairs. **Three diagnostics passed**
on disposable PostgreSQL 18.6; container removed. They are new design evidence only, not EF
replacement tests. The proposal records their SQL predicate/interleavings and the required native
EF/consumer proofs. No new durable harness, runtime token, changed public interface or unsupported
claim of provider portability was introduced.

Recommended replacement removes ConfigureRebuilding from writing, exposes an independent
rebuilder, uses a technical header stamp/native predicates, removes shared/exclusive admission
and the public Prepare/Stage handles, and replaces EF persistence packets with direct native
row operations plus a small exact save association. It permits conflicts rather than blocking
pre-load writers, so the changed public/schema/online behavior must be owner-reviewed before
implementation. A library maintenance worker remains planned and deferred, not rejected.
Existing index entries were preserved; this cleanup and proposal remain unstaged. No commit.
