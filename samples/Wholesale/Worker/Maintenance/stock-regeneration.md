# Explicit stock-position regeneration

This is ES3's finite maintenance journey for one existing Inventory aggregate. It uses
the provided `AggregateRegenerator` through Inventory's technical `StockPositionMaintenance`
entry and the current full-prefix rebuilder. The existing `IStockPositionRebuilding` remains
a caller-saves staging Contract. Ordinary reads and commands
keep their strict inline-state contract; they do not invoke this command or repair state.

Use an already migrated PostgreSQL database and the corrected deployed reducer/codec. The
local operator supplies an authorized Organization key and stream GUID. This console trusts
that selection and its database credentials: initializing tenant context is not authorization.
There is no HTTP endpoint or worker admission change.

```sh
export WHOLESALE_DEMO_CONNECTION_STRING='<existing PostgreSQL connection string>'
dotnet run --project samples/Wholesale/Worker -- \
  --regenerate-stock rebuild-demo e5020000-0000-0000-0000-000000000001
```

Supply an existing stock-position identity created through the Inventory product operations. Maintenance does not migrate, seed, open a
missing stream or register ordinary command/query services. It selects one Inventory context;
Wrong Organization/unknown GUID returns not-found.

## Progress and outcomes

Each flushed stdout line is JSON with `invocation`, `organizationKey`, `stockPositionId`,
`attempt`, `phase`, nullable `version` and nullable `reason`. Invocation IDs change on restart;
target identity stays explicit. Output contains exception types, not provider messages or
connection strings. Redirect stdout to retain an operator log; the application does not
persist jobs or claim that its output is a transactional completion ledger.

| Phase | Meaning |
| --- | --- |
| `started` | A fresh attempt is opening its operation scope/transaction. |
| `replayed` | The complete captured prefix has been reconstructed and staged; nothing has committed. `version` identifies that captured head. |
| `conflict` | A known native stream conflict rolled back/disposed; another attempt will use a fresh scope and replay. |
| `completed` | Save and commit were acknowledged. `version` is the repaired head at that commit, and can advance again immediately through ordinary writers. |
| `not-found` | No stream exists within the selected Organization filter. |
| `failed` | A nonretryable pre-commit error, or three conflicting attempts. `reason` distinguishes writer conflict from exception type. |
| `cancelled` | Cancellation before commit; the attempt's native transaction/context have been disposed. |
| `indeterminate` | Commit began but its acknowledgment failed/cancelled. The repair may have committed. Inspect/repeat explicitly. |

Exit status is 0 for completed, 1 for failed, 2 for not-found, 4 for indeterminate, 64 for
invalid command arguments/missing connection, and 130 for pre-commit cancellation. Command
syntax failures go to stderr before opening a database. Progress is per attempt/full replay;
there is no per-event percentage, resumable prefix checkpoint or duration estimate.
Gloaming returns typed terminal results; the host formats these JSON lines and exit codes.
Completed can carry a cleanup diagnostic reason while retaining the acknowledged commit.
The host supplies a synchronous `IProgress<AggregateRegenerationProgress>` reporter. The
executor emits Report calls in phase order; delivery depends on the reporter. Standard
`Progress<T>` may deliver asynchronously, even after the operation returns. Its asynchronous
handler errors are outside that returned outcome, so the CLI uses inline reporting for ordered,
flushed JSON. The terminal result remains the completion authority.

## Cancellation, restart and writers

Ctrl+C passes cancellation through replay, SQL/save and retry waits. If cancellation arrives
during commit, the outcome is indeterminate. An acknowledged commit wins over a later signal.
A hard process kill may leave only `started`/`replayed` in the log; absence of `completed`
does not establish rollback. PostgreSQL releases an abandoned transaction when its connection
ends. Rerun the same command after cancellation, process death, correction of a failure, or an
uncertain acknowledgment. It reconstructs the entire current retained stream again. A repeat
can change the technical concurrency stamp even when business state was already correct.
No event or business effect is emitted by replay; facts/head/recorded times are retained.

At most three attempts run, separated by cancellation-aware constant delays of 100 ms. Gloaming
owns fresh scopes/native read-committed transactions, replay/save/commit, disposal and recognized
conflict retry. The host selects the retry values and establishes the already-authorized tenant
in each scope. A writer advancing between header/state reads is retryable only after a fresh
header read confirms a new stamp/advanced head covering that state. Persistent ahead state,
unrelated concurrency and history/schema/SQL constraint failures stop. A provider-specific
unique violation from competing repairs of missing state also stops; inspect/restart explicitly.
Cancellation reaches IO/stage boundaries; synchronous evolution runs to its next stage boundary.

Inventory's native scope-factory decorator publishes the selected tenant before returning each
fresh scope to the executor. It disposes the scope if establishment fails, retaining both
initialization and cleanup errors when needed. Such failure produces terminal failed progress
without started and counts one attempt. Context-establishment services on this path are
synchronous; asynchronous admission completes before selecting the maintenance operation.

Online maintenance uses existing header Version/ConcurrencyStamp concurrency. A writer can
invalidate a captured repair; a committed same-version repair invalidates already-decided
writers. Continuous writes can exhaust this finite budget. The operator owns any pause/drain
window and consistent reducer deployment. This command does not exclude all writers before
read, fence raw SQL, repair bad retained events, undo previously committed bad decisions or
make mixed reducer versions safe. No scheduler, queue, lease, aggregate discovery, async view
engine, new-service bootstrap or template runtime is supplied.

## Executable proofs

`StockRegenerationTests` is part of the existing EventSourcing CI consumer suite. It uses
PostgreSQL 18.6 Testcontainers, consumer Contracts for reads/writes and native interceptors/SQL
barriers for faults. Linux child-process cases exercise SIGINT, hard kill and explicit restart.
The commit-acknowledgment case injects an exception after a real commit; it is not a network
partition test. Existing ES2 evidence is distinct from these new ES3 executions.
The independent [ledger executor proofs](../../../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/RegenerationTests.cs)
exercise initialization order, narrow classification, native completion and cleanup without
Inventory, tenancy or codec assumptions. See the [package contract](../../../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md#complete-regeneration-operations).

```sh
dotnet build samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
  dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj \
  --no-build --no-restore --filter-class '*StockRegenerationTests'
```

Use your reachable Docker-compatible socket on other hosts. See the
[scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es3-aggregate-regeneration-operations.md),
[slice report](../../../../docs/reports/es3-aggregate-regeneration-operations.md) and
[current rebuilder contract](../../../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md).
