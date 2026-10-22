# ES3: explicit aggregate regeneration operations

Initial scope selected by the owner's ES3 request, 2026-10-17. Baseline: fetched `origin/main`
`4633355`. The first journey is implemented as consumer composition over the current rebuilder.
The owner subsequently reviewed and explicitly approved the optional executor interface; its
approved scope follows below and is now implemented. The CLI adopts the library executor.
The owner approved the native IProgress/initialized-scope-factory refinement on 2026-10-18;
the usage and obligations below describe that current interface.
Earlier ES2 gate/secondary-view proposals are superseded by ADR 0008 and the current native EF contract.

## Consumer first

```sh
WHOLESALE_DEMO_CONNECTION_STRING='<existing migrated PostgreSQL database>' \
  dotnet run --project samples/Wholesale/Worker -- \
  --regenerate-stock rebuild-demo e5020000-0000-0000-0000-000000000001
```

One trusted local operator selects one admitted Organization and one existing Inventory stock
position. The command registers only Inventory database access and its existing maintenance
Contract, establishes immutable tenancy in each fresh scope, begins a native transaction,
calls `IStockPositionRebuilding`, saves and commits. Ordinary query/write paths retain their
existing strict state checks. Maintenance performs no migration, seeding or command dispatch.

## Bounded policy

- One invocation, one target, at most three attempts. Retry only the module's existing native
  stream-conflict classification, with a short cancellation-aware delay and fresh scope,
  context, transaction and full replay. No coordination beyond the existing concurrency stamp.
- Flush JSON lines with invocation/target identity, attempt and phase. Replayed means prepared;
  completed means acknowledged commit. Include terminal missing/failure/cancellation outcomes.
  No event percentage is claimed: the current rebuilder returns only complete-prefix metadata.
- Ctrl+C cancels SQL/replay/save and retry waits. Dispose the attempted transaction/context
  before reporting a failure or retry. A failure during commit is indeterminate and is never
  automatically retried or described as rolled back.
- Restart means explicitly rerun the same command, including after process death or an uncertain
  acknowledgment. Replay is effect-free and converges to retained history; a healthy repeat may
  change the technical stamp again. There is no durable request queue or resumable event cursor.
- Online optimistic maintenance allows writer overlap and can exhaust its retry budget. The
  operator owns admission, privileges, reducer rollout and any pause/drain of writers. A wrong
  command committed before repair cannot be undone by rebuilding.

## Proof and change boundary

Use real PostgreSQL 18.6 with the existing Wholesale suite/CI lane. Demonstrate consumer reads
and commands after repair; unchanged retained facts/head/timestamps; missing and foreign targets;
nonretryable history/SQL failure; cancellation/rollback and fresh restart; deterministic writer
conflicts, bounded exhaustion and stale-writer rejection; and process death/restart around the
actual executable. Test barriers use native SQL/interceptors, without production test hooks.

The initial changes belong to the finite executable, its adoption tests and documentation. No migration,
generic job/scheduler/lease framework, provider locking package, projection engine, new public
library interface or template preset is selected. Extract only mechanics the journey actually
proves. Any necessary library interface change must first receive owner review.

## Executor proposal for owner review

This section records the owner-approved scope, now implemented in the existing EventSourcing
EF package. The canonical [reviewed public surface](library-extraction.md#es3-optional-regeneration-executor-interface-review)
is in the extraction plan, with [current setup](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md#complete-regeneration-operations)
beside the package. Existing core interfaces, direct staging callers, command writes,
module Contracts, migrations, schema and package dependencies retain their contracts.

### Concrete consumer usage

Inside the Inventory implementation assembly, where the aggregate type is already available:

```csharp
// The CLI's trusted operator/admission has already selected this Organization.
var tenant = TenantContext.ForTenant(new TenantId(organizationKey));
var initializedScopes = new OrganizationScopeFactory(scopes, tenant);
var regeneration = new AggregateRegenerator<StockPositionAggregate, InventoryDbContext>(initializedScopes);
var result = await regeneration.RegenerateAsync(
    id,
    new AggregateRegenerationOptions
    {
        MaxAttempts = 3,
        RetryDelay = TimeSpan.FromMilliseconds(100),
    },
    progress: progress, // IProgress<AggregateRegenerationProgress>
    cancellationToken: cancellationToken);
// Format the returned terminal outcome; Completed means acknowledged commit.
```

A small public technical `StockPositionMaintenance` entry in the Inventory implementation
assembly contains this wiring and the native scope-factory decorator without exposing its
internal aggregate or adding technical
dependencies to business Contracts. The external CLI calls that entry and owns progress/terminal
JSON and exit mapping. Its current scope/transaction/retry loop is deleted rather than layered
around the executor. The existing `IStockPositionRebuilding` remains a caller-saves staging role.

The independent tenant-free ledger consumer uses the same executor with its `LedgerDatabase`
and the ordinary native scope factory. This checks the absence of Inventory, tenancy and codec assumptions; it is
not a second product journey or a mandatory second consumer before proposing an extraction.
Native DI resolves the explicitly selected context and existing rebuilder. The executor rejects
a replay associated with a different context/transaction before save.

### Ahead-state race and conflict classification

The initial consumer behavior confused two distinct observations: header version 3 followed by state
version 4 because a normal receipt committed between those reads; and header version 3/state
version 4 that were persistently inconsistent before maintenance. Both initially threw
`InvalidDataException`; the baseline executions confirmed the gap. The final proof now requires
automatic fresh replay only for the verified writer race, with corruption remaining terminal.

Approved and implemented rebuilder behavior only when the state is ahead of its captured header:

1. Re-read the filtered header in the same native read-committed transaction without tracking,
   using the same complete logical target/filter as the original header read.
2. If the header is valid, its event version advanced, its stamp changed, its creation metadata
   still identifies the same stream and its head covers the observed state version, report a
   verified stale capture through an internal subclass of native `DbUpdateConcurrencyException`.
   The internal exception identifies the attempted context/target for the executor; no public
   exception type or string-based classification is needed.
3. Otherwise retain `InvalidDataException`. A stamp change alone cannot turn a genuinely ahead
   state into valid data. No history or arbitrary integrity exception is broadly retried.

The executor selects native read committed explicitly for its own transaction so this re-read
can observe the committed writer. Direct rebuilder callers keep ownership of isolation and
save/commit; a repeatable-read transaction can retain its existing integrity observation.
All retries discard the entire context/transaction and read the full current prefix again.
Native save conflicts are accepted only when their nonempty entries belong to the current
private maintenance association. An initializer, decoder or custom consumer cannot manufacture
an unrelated native concurrency exception and cause an automatic retry.

This does not guarantee automatic retry for every possible provider error: two repairs inserting
the same missing state can hit a provider-specific unique constraint before a header conflict.
Such an error remains a terminal `Failed` outcome, with explicit restart possible. The writer
journey does not require a new Npgsql dependency or broad SQL-error retry to solve that distinct
case. Progress and result documentation must state this limit.

### Lifecycle and reporting rules

The executor snapshots/validates options once. At each attempt: check cancellation, create a
fresh scope whose factory establishes consumer context, resolve the typed native dependencies, begin read
committed, replay, verify native association, save, check cancellation and commit. Retry only
after attempting disposal of both transaction and scope. No current operation's context is
carried into a new scope or rebound. No preexisting transaction is adopted.

Terminal outcomes distinguish acknowledged completion, missing target, exhausted recognized
contention, cancellation before commit, other failure and commit uncertainty. The current CLI
exit codes stay 0/2/1/130/1/4 respectively. Structured errors are formatted by the consumer;
provider payload/credential text is not automatically logged. Started/replayed/retrying Report
calls occur in order, with retry reporting after cleanup. Started follows successful initialized
scope creation. The factory owns cleanup if initialization fails before return; the outcome is
Failed with one attempt and no Started report. Inventory resolves only synchronous establishment
services on this path. The CLI supplies synchronous IProgress to flush ordered JSON; native
Progress<T> can deliver later. Synchronous Report errors stop execution, while asynchronous
handler errors are outside the returned outcome. There is no terminal Report call inside the
commit/cleanup path: the caller formats the returned result.

The supplied sample uses a constant 100 ms delay, replacing its initial 100/200 ms backoff.
That value is explicit consumer policy, not a package default. Defaults permit one attempt
without delay. Cancellation is checked at IO and stage boundaries; synchronous `Rehydrate`
remains an uninterrupted call. Full history is still materialized in memory. Neither large-stream
throughput nor cancellation latency inside arbitrary consumer evolution is claimed or expanded
by this executor. Chunked evolution/checkpoints require separate reviewed interfaces and proofs.

### Approved file and proof scope

| Files | Change |
| --- | --- |
| EF package: `AggregateRegenerator.cs` (new) | Own one complete regeneration operation using fresh native scopes/typed context and the existing rebuilder. |
| EF package: `AggregateRegenerationOptions.cs`, `AggregateRegenerationResult.cs`, `AggregateRegenerationProgress.cs` (new) | Exact types/enums from the extraction plan; no provider/host/tenant dependency. |
| EF package: `AggregateRebuilder.cs`, private maintenance association in `RequiredInlineStateExtensions.cs` | Distinguish verified advanced-header race from persistent ahead-state damage; narrowly recognize the current attempt's native save conflicts and verify context pairing. |
| Inventory implementation: `StockPositions/StockPositionMaintenance.cs` (new); existing sample host | Establish selected immutable tenancy per attempt, invoke executor and replace the duplicate host loop; business Contracts stay independent. |
| Family PostgreSQL tests/ledger consumer | Adopt the same public executor without tenancy/codec; verify options, fresh scopes, context pairing, narrow conflict classification and all lifecycle/result guarantees. |
| Wholesale adoption tests | Preserve existing CLI/process proofs; replace the baseline writer-race failure expectation with automatic fresh replay, while retaining terminal corruption controls. |
| Package/family docs, design, extraction/roadmap and report | Mark proposals versus supported behavior, document ownership, research assumptions and new executions only after implementation. |

Required proofs include the existing repair/writer/cancel/process/acknowledgment scenarios through
the executor, plus writer commit between header/state reads, persistent ahead state, a changed
header that still does not cover corrupt state, nonretryable unrelated concurrency and constraints,
scope-factory/synchronous-Report failures, delayed native Progress<T> delivery/errors, invalid
options, zero-attempt cancellation, initialization failure cleanup, correct scope disposal,
typed context/rebuilder mismatch, and known-commit preservation during cleanup failure. No production
test hooks are needed; use native EF interception and real PostgreSQL as in the initial proofs.

### Evidence and assumptions

The research supports explicit single-stream rebuilding as a library capability
([Marten](https://martendb.io/events/projections/rebuilding.html#rebuilding-a-single-stream)) and
application-managed optimistic tokens/re-query after contention
([EF Core](https://learn.microsoft.com/en-us/ef/core/saving/concurrency#application-managed-concurrency-tokens)).
These are design references, not proof of the Gloaming executor. The original Marten report
uses moving references rather than a pinned source release; no cross-version guarantee follows.
The initial ES3 PostgreSQL/process results demonstrate the extracted lifecycle obligation, while
new executor/independent-consumer results are recorded separately in the
[slice report](../reports/es3-aggregate-regeneration-operations.md).

Retained history must be complete/decodable, and historical evolution must be deterministic
and free of external effects. These are explicit consumer obligations, not facts that external
research or generic tests can prove for arbitrary reducers. Mixed reducer deployment, badly
committed business facts, admission, durable scheduling, large-stream performance and external
writers bypassing the native contract remain outside this scope.
