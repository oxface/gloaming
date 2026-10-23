# Outbox completion-failure supervision

Date: 2026-10-21. Owner-selected follow-up to approved M2 checkpoint `6bcdf9c`, implemented
on `feat/outbox-completion-supervision` for line-by-line review. The approved checkpoint
passed every repository commit hook. This report records subsequent evidence separately
from [M2 outbox dead letters](m2-outbox-dead-letters.md).

## Outcome and public contract

Publication returns normally, but recording completion can fail. The optional worker now
faults without another dispatch poll on OutboxCompletionException, logging a distinct critical
diagnostic with MessageId. Native StopHost/Ignore governs the host. This contains the worker's
republication loop when completion storage is persistently broken.

The [exception](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxCompletionException.cs)
retains MessageId and the original completion error as InnerException without retaining the
envelope. Completion may already have committed. Direct consumers own equivalent containment,
inspection and restart policy. Callers previously catching native completion SQL/IO errors must
now inspect InnerException. Completion-stage requested cancellation also carries this typed
signal; its attempt telemetry remains cancelled. Publication cancellation retains its original
OperationCanceledException contract. Requested worker shutdown ends normally without another poll.

Only the existing completion operation is wrapped. Claim and publisher exceptions retain their
existing paths. Completion faults never invoke publication-failure policy, change failure
counts, dead-letter work or issue a compensating lease release. ClaimLost remains an ordinary
result when the fence is lost, and the worker continues in a fresh scope. Publication failures
whose accounting succeeds still use FailureDelay and fresh scopes.

## Executable adoption and proof boundaries

[OutboxDemo](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo/README.md#dead-letters-and-operator-retry) already composes
the optional worker with explicit native StopHost. Its existing `--setup` and `--serve --worker`
commands adopt the behavior without configuration changes. Documented inspection uses fresh
metadata: pending work waits for lease expiry, completed work requires no action, and a dead
letter still needs the existing authorized conditional retry. That API cannot retry pending
or completed work. No new recovery interface, migration, grant or dependency was added.

Tests use the existing callable dispatcher and native worker/host seams, real PostgreSQL 18.6,
the public mapped row and actual HTTP/RabbitMQ transport boundaries. The failure matrix proves:

- Completion SQL rejection and response loss after actual PostgreSQL completion commit both
  produce the typed fault. Both StopHost and Ignore leave a faulted worker after one publication;
  StopHost also requests host shutdown. Ignore does not restart the worker.
- Rejected completion retains its committed claim, Attempts and Revision; lost commit response
  instead leaves completion committed. Neither invokes the throwing publisher-failure policy
  or changes failure/dead-letter evidence. Existing conditional Retry returns NotApplied.
- A native row-lock barrier, verified through pg_blocking_pids, establishes blocked completion
  before cancellation or pg_terminate_backend. Direct callers retain the stage and original
  error. Fresh dispatch after expiry recovers the same identity. Cancellation reports cancelled
  telemetry; backend death reports failed. No elapsed-time assumption establishes the race.
- Shutdown while completion is blocked ends the worker successfully after one publication and
  leaves the lease recoverable, without a false completion or failure observation.
- The built OutboxDemo child process sends to real Kestrel HTTP, receives 202 acceptance,
  encounters completion SQL rejection, emits the distinct diagnostic and stops through native
  supervision. Repair/restart waits for the retained lease and publishes the identical message/
  payload. Attempts advances from one to two; FailureCount and RedriveCount stay zero.
- Existing PostgreSQL competition, expired/stale-lease fencing, repeated operator retry and
  accounting-fault supervision remain passing regressions. The ordinary worker retry proof now
  separately covers ClaimLost without stopping or consuming the publisher-failure budget.
- The actual RabbitMQ/OTLP process proof now observes automatic completion-fault host shutdown
  before restart, retaining its trace/metric/log and receiver-deduplication assertions.

## Verification

Fresh successful runs, with zero failed/skipped tests:

| Suite | Passed |
| --- | ---: |
| OutboxPostgresTests, complete | 82 |
| MessagingWorkerDemo.Tests, complete | 13 |
| OutboxDemo.Tests, complete | 12 |
| WorkflowDemo.Tests, SalesDispatchTests | 34 |
| EventPersistenceDemo.Tests, *DispatchTests | 18 |
| LocalWorkflowDemo.Tests, complete | 14 |
| ArchitectureTests, complete | 96 |
| **Total** | **269** |

The restricted Sales proofs verify dispatch credentials, effective privileges and accounting
faults without granting operator-only redrive or business/payload mutation. RabbitMQ and local
delivery regressions exercise actual independent consumer transactions and receiver admission.
These are new runs of the existing adopters; their broader historical reports remain separate.

The complete active solution builds with zero warnings/errors. Full solution style and analyzer
verification, CSharpier (884 files including generated source), git diff --check, and changed
Markdown whitespace/local-link checks all pass. The implementation was
developed through a failing PostgreSQL typed-fault proof and four failing native-host
supervision cases, then their passing runs. An initial child-process test build exposed the
xUnit requirement to reference its timeout cancellation token directly; that was corrected
before the successful 13-test process run. These failures are not counted as passing evidence.

Reproduce from the repository root with the configured Docker-compatible runtime:

```bash
dotnet build ModulithFoundry.slnx --no-restore
dotnet test --project src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Messaging/samples/OutboxDemo.Tests/OutboxDemo.Tests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-build --no-restore -- --filter-class '*SalesDispatchTests'
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-build --no-restore -- --filter-class '*DispatchTests'
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-build --no-restore
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj --no-build --no-restore
dotnet format style ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet format analyzers ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet csharpier check . --include-generated
git diff --check
```

## Extraction findings and limits

The new reusable behavior is a completion-stage fault signal and optional worker containment.
Native .NET already provides the host supervision mechanism. The consumer owns transport
acceptance definitions, repair, credentials, fresh-state inspection, alert/readiness behavior,
process restart policy and operator authority. Existing dead-letter interfaces retain their
different inbox/outbox storage semantics; no new recovery abstraction or shared queue engine
was justified. Samples need only their existing explicit registrations. No template facility
or new persistence mechanism was proven or added.

Stopping one worker pauses its unrelated work too. Other dispatchers and repeated external
restarts can continue to publish after expiry; this is no durable global execution cap.
Receiver deduplication remains necessary, and a completion fault does not prove downstream
business success or rollback. No host exit-code or bounded-shutdown guarantee is added.
Backlog/health monitoring, retention, queued-schema rollout, lease renewal and dispatch
parallelism remain separate capabilities. Payload editing and completed-message replay remain
excluded.

Review the exception, [callable boundary](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs),
[worker](https://github.com/oxface/modulith-foundry/blob/d63f5678501fc9c33d42ad8104c1a06faf8fa8fd/src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxWorker.cs),
[PostgreSQL proofs](../../src/Gloaming.Messaging/tests/OutboxPostgresTests/CompletionSupervisionTests.cs),
[child-process proof](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/OutboxRecoveryProcessTests.cs) and
[family contract](../../src/Gloaming.Messaging/docs/outbox-recovery.md) first. The
[scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/m2-outbox-completion-supervision.md) records concrete usage and file/schema
boundaries. This follow-up remains unstaged and uncommitted; its originally empty index is
preserved. No push or pull request was requested.
