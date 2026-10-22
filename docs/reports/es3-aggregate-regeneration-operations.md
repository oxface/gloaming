# ES3: explicit aggregate regeneration operations

Initial implementation: 2026-10-17; approved interface refinement: 2026-10-18.
Baseline: fetched `origin/main` `4633355`. Branch:
`feat/aggregate-regeneration-operations`, created without upstream tracking in the requested
checkout. The owner explicitly approved the executor interface before library implementation.
Implementation is ready for line-by-line review. The owner staged reviewed work before the
2026-10-18 refinement; this follow-up preserves that index and leaves its edits unstaged.
No commit or push was performed.

## Outcome and executable adoption

`Gloaming.EventSourcing.EntityFrameworkCore` now provides the optional
`AggregateRegenerator<TAggregate,TContext>`. It owns one complete maintenance operation using
fresh native scopes/read-committed transactions, the existing full-prefix rebuilder, save/commit,
cleanup, bounded recognized-conflict retries and typed progress/results. No new package or
dependency was added. Direct rebuilder callers retain caller-owned save/commit; ordinary
reads and commands remain independent and strict about required current state.

The [stock-position command](../../samples/Wholesale/Worker/Maintenance/stock-regeneration.md)
adopts the executor through Inventory's technical `StockPositionMaintenance` entry:

```sh
WHOLESALE_DEMO_CONNECTION_STRING='<existing migrated PostgreSQL database>' \
  dotnet run --project samples/Wholesale/Worker -- \
  --regenerate-stock rebuild-demo e5020000-0000-0000-0000-000000000001
```

The CLI selects one admitted Organization and existing stock position. Inventory's consumer-owned
native scope-factory decorator initializes that immutable tenant before returning every fresh
scope. The CLI selects three attempts with constant 100 ms retry delay and supplies an inline
IProgress reporter. It
formats flushed JSON progress/terminal results and existing exit codes. Its duplicate transaction/
retry loop is removed. No migration, seed, ordinary command or event publication occurs in this
maintenance entry. Business Contracts gain no technical dependency.

An independent [direct-JSON ledger adopter](../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/RegenerationTests.cs)
uses the same public executor without Wholesale, tenancy, actor or codec assumptions. The
[package contract](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md#complete-regeneration-operations)
is self-sufficient; [reviewed interface/scope](../plans/library-extraction.md#es3-optional-regeneration-executor-interface-review)
and [ADR 0017](../adr/0017-explicit-aggregate-regeneration-execution.md) record ownership decisions.

## New PostgreSQL and process proofs

There are **42 new ES3 cases**: 25 independent executor cases plus 17 Wholesale maintenance cases.
These use real PostgreSQL 18.6, native EF interception/SQL barriers and Linux child processes;
they are separate from external research and historical ES2/archive evidence. Existing regressions
were also rerun in the final verification below. No production test hook was added.

| Obligation | New evidence |
| --- | --- |
| Complete independent library operation | Direct ledger adoption reconstructs missing/corrupt state, uses native read committed, saves/commits, returns Completed and supports repeat execution. Ordered Report calls carry only attempt progress; terminal result returns after cleanup. |
| Maintenance-only Inventory wiring | Command/query services are absent from the maintenance provider. Setup needs Inventory only. Reads and later receipt commands through consumer Contracts use repaired state. |
| Preserve retained facts/head/times | Wholesale compares full retained event/header JSON excluding only the technical stamp. Repeated repair changes that stamp without appending facts or changing business version/timestamps. |
| Missing/foreign targets | Terminal not-found with no header/history/stamp mutation under the selected Organization filter. This exercises existing native filtering, not a new database-role/RLS isolation guarantee. |
| Fresh scopes and initialization order | A real ordinary writer invalidates the first save. The consumer-owned factory initializes before context options resolve and once in each distinct scope. All failed contexts are disposed before retry progress; a new context captures the updated head. |
| Writer between header/state reads | A real receipt commits after header capture and before inline read. Rebuilder re-reads the header, verifies advance/new stamp/complete key/creation metadata/covering version, and executor performs fresh replay. The original baseline reported integrity failure for this schedule; the final test now requires automatic recovery. |
| Persistent or insufficiently covered ahead state | Unchanged head with an ahead state stays terminal. Independent controls also change the stamp or advance a real writer while leaving state ahead of the new head; neither produces an automatic retry. |
| Bounded contention and stale decisions | Actual receipt commits invalidate all three attempts and return conflict/failure without losing writer facts. Explicit restart succeeds after contention stops. A committed same-version repair rejects an already-decided writer; a fresh ordinary command succeeds. |
| Narrow failure classification | Real concurrency exceptions from another context's normal ledger write do not retry, whether raised during replay or save. Scope-factory and synchronous start/replayed/retrying Report concurrency errors remain failures. Bad pairing of rebuilder and selected native context fails before save. |
| Failed scope establishment | Independent and Inventory factories dispose scopes when initialization throws before return. The outcome is Failed with one attempt and no Started report. Inventory also preserves both establishment and disposal failures; retained facts/header stamp are unchanged. |
| Deferred native Progress delivery | Standard Progress<T> posts to a controlled SynchronizationContext. Real PostgreSQL regeneration completes before notification delivery. Later delivery and a handler failure leave the returned Completed outcome unchanged; async handler errors are not presented as operation failures. The CLI's inline reporter preserves ordered/flushed JSON in the process proofs. |
| Integrity and provider failures | Unsupported retained schema and real PostgreSQL CHECK failures each stop after one attempt, retain data/stamp and recover after explicit fault correction/restart. Arbitrary constraints are not writer contention. |
| Cancellation and retry delay | Cancellation after actual repair SQL rolls back before commit and permits fresh restart. Pre-cancelled execution has zero attempts and resolves no operation services. Cancellation during retry delay starts no new scope. |
| Executable interruption/restart | PostgreSQL row lock blocks the actual child CLI after flushed replayed progress. Real SIGINT exits 130; hard kill exits abnormally. After connection/lock release, damaged state/stamp are unchanged and two new process invocations converge. Linux proof, not cross-platform signal certification. |
| Commit uncertainty | Native interceptor throws after a real PostgreSQL commit, simulating lost acknowledgment. Result is indeterminate/exit 4 without automatic retry; committed state is readable and explicit replay preserves facts/head/times. This is not a real network-partition test. |
| Cleanup and known completion | Scope-disposal failure after acknowledged commit retains Completed with diagnostic Error. Cleanup failure following pre-commit cancellation preserves cancellation and retains both errors. Native contexts are disposed and no automatic retry occurs. |
| Validation/ambient ownership | Invalid attempt/delay/null/empty arguments are rejected before scope creation. An ambient transaction is rejected before consumer initialization. |

## Final verification

All commands below ran against the completed C# implementation, using SDK 10.0.401 and
`DOTNET_PROCESSOR_COUNT=4`. Local builds used `-m:1 -nr:false`. Test runs used the documented
Podman socket outside the socket-restricted sandbox. Existing CI paths already include these
source/consumer/test folders; no new CI lane is needed.

| Check | Result |
| --- | --- |
| Active solution build, `--no-restore` | Passed, zero warnings/errors. |
| EventSourcingPostgresTests | **92 passed**, including all 25 independent executor cases. |
| Wholesale EventPersistenceDemo.Tests, excluding DispatchTests as in EventSourcing CI | **189 passed**, including all 17 ES3 maintenance/process cases. |
| EventStorageDemo.Tests | **35 passed**; independent raw stream/consumer regression. |
| EventSourcingTests | **11 passed**; aggregate core regression. |
| ArchitectureTests | **96 passed**. |
| Native style and analyzers on changed/new C# paths with verify-no-changes | Passed. |
| CSharpier | Passed on the five C# files changed by the ergonomics refinement. |
| Whitespace and local Markdown targets | Passed; no missing local link targets. |
| Frozen archive preservation | Passed; all 800 original files verified against the recorded baseline. |
| Staged snapshot | Owner's reviewed snapshot unchanged; index checksum matches the pre-refinement capture. |

Total: **423 tests passed, 0 failed, 0 skipped**, freshly rerun after the 2026-10-18 refinement.
RabbitMQ dispatch suites and unrelated runtime/browser suites were not rerun for this slice.

## Reusable mechanics versus consumer policy

The 2026-10-18 refinement replaces the Action progress parameter with native IProgress and
removes per-call scope initialization. It introduces no additional reusable mechanism: it
refines the executor interface and moves context establishment to consumer-owned native
scope factories. Five added cases establish the new delivery/initialization-failure semantics.
The earlier 418-test result remains historical evidence, not an additional current test count.

**One new reusable mechanism is demonstrated:** complete explicit regeneration execution over
the existing provided rebuilder. Fresh attempt scopes, transaction/save/commit ownership,
exact-context verification, narrow conflict retry, cleanup and typed outcomes now live in one
library implementation exercised by both consumers. This adds no generic maintenance scheduler,
job registry, discovery protocol, lease engine or native transaction abstraction.

Consumer policy remains target/Organization admission, trusted operator/database privileges,
attempt/delay values, progress presentation, exit codes, logging, scheduling, consistent reducer
rollout and online/pause/drain decisions. The technical Inventory entry establishes selected
tenancy without exposing its internal aggregate or coupling its business Contracts to the library.
The independent adopter proves that tenancy/Wholesale/codec setup is optional.

Library findings: the original rebuilder/concurrency/save association were sufficient building
blocks; the optional executor removes demonstrated repeated completion obligations. Native
concurrency protects cooperative writes without another coordination mechanism. Template
findings: no new template preset or event runtime is justified; the event-free template is
untouched. Sample findings: one real maintenance journey now adopts the provided executor while
preserving ordinary read/write behavior and native consumer-owned domain policy.

The initial consumer-only report concluded that no new mechanism had been implemented. It was
too restrictive to infer that all runner logic should remain consumer-owned. Before interface
approval, two additional PostgreSQL baseline cases confirmed the writer-read-race classification
gap: both racing and persistently ahead state then failed as InvalidDataException. The final
implementation/proofs above supersede that behavior. Earlier 185-test/96-test and 15-case baseline
executions are historical evidence, not additional tests added to the final count.

## Review-worthy files and remaining gaps

- [AggregateRegenerator](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateRegenerator.cs),
  [options](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateRegenerationOptions.cs),
  [progress](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateRegenerationProgress.cs) and
  [results](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateRegenerationResult.cs): approved public surface and operation lifecycle.
- [AggregateRebuilder](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateRebuilder.cs) and
  [RequiredInlineStateExtensions](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/RequiredInlineStateExtensions.cs): verified stale-capture classification and reuse of the exact private maintenance association.
- [StockPositionMaintenance](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionMaintenance.cs) and
  [CLI host](../../samples/Wholesale/Worker/Maintenance/StockRegeneration.cs): domain-independent execution delegated to Gloaming; admission, retry values and presentation remain visible.
- [Independent proofs](../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/RegenerationTests.cs) and
  [Wholesale proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/StockRegenerationTests.cs): actual failures, writers, scopes and process recovery.
- Package/family docs, guide, reviewed scope, roadmap/design and ADR: supported behavior versus deferred work.

Assumptions remain explicit: retained history must be complete/decodable and historical evolution
must be deterministic and free of external effects. External research supports single-stream
rebuilding and native optimistic-token mechanisms; it cannot prove arbitrary reducers correct.
The original Marten source references were moving, not a pinned release, and are not a certification
of this implementation. New PostgreSQL/process evidence supplies the narrower guarantees above.

Full history is still materialized in memory; synchronous evolution has no mid-fold cancellation
check. Large-stream throughput/cancellation latency remain unproven. Continuous writers can exhaust
the budget. Provider-specific unique violations from competing missing-state insertions may fail
before a header conflict and are not automatically retried. Logs are not an atomic completion ledger;
there is no durable request/cursor, automatic uncertain-commit reconciliation, multi-target scan,
new-service population, mixed-reducer deployment safety, external-writer fencing or repair of bad
retained/business facts. No second database provider or cross-platform signal guarantee is claimed.

The original 2026-10-17 staged snapshot was empty. The owner subsequently staged reviewed work.
At the start of the 2026-10-18 refinement its index checksum was
`1d03e458865077c6c490683be98c2e81eed0198af0bb26dbf1ab6b0598b8dbf9`; it remains unchanged.
All follow-up edits remain unstaged, and the existing untracked executor remains untracked.
No commit or push was performed, and no unrelated worktree or archived source was changed.
