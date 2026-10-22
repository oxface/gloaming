# W1: separate native worker hosts

Date: 2026-10-09. Implementation ready for owner review on `feat/separate-worker-hosts`.
Base `29a899d` from merged `origin/main`; the remote has no `master` branch. All changes remain
unstaged and uncommitted. This report authorizes neither checkpoint nor publication.

Follow-up, 2026-10-22: the [broker recovery/shutdown closure](w1-broker-recovery-shutdown.md)
adds live broker outage, fresh-session progress and blocked-publication shutdown evidence.
The executions and results below remain historical W1 evidence.

## Outcome and scope

The existing Messaging APIs run outside an API without a new worker library or coordination
protocol. [The producer](../../samples/MessagingProducerDemo/Program.cs) registers Exports-only
enqueue and native command transactions. [The worker entry point](../../samples/MessagingWorkerDemo/Program.cs)
selects finite setup, outbox dispatch, RabbitMQ intake or inbox processing. The three worker
roles run in separate processes and receive only their respective module/transport settings.
Processing does not need broker or producer configuration. The existing finite demos remain.

Setup applies each module's existing migration history separately, declares one configured
durable queue and exits without creating business data. Daemon startup read-checks required
tables and passively checks the queue; it does not migrate, seed or own queue deletion.
API liveness is process liveness, not a dependency readiness contract.

`ExportDbContext.Configure` shares its existing native Npgsql/history configuration with
the new hosts, preserving `Create` and all mappings/migrations. The producer accepts draft
creation and versioned, state-dependent submission. Exports owns that decision; Rendering
owns its wire decoder, admission and local job handling. Each has its own context/transaction.
No peer business-table access or shared transaction is used.

The sample owns one long-lived connection/channel per transport role and reuses the I1
native RabbitMQ adapters sequentially. Publication confirms precede outbox completion;
retained intake commits before native acknowledgement. The intake loop stops nonzero on
unhandled intake failure without acknowledging the message. Native host cancellation and
disposal release this process's resources. A permanently closed publishing channel requires
operator/supervisor restart; the existing outbox worker continues its documented retry loop.

No Gloaming interface/implementation or package version changed. The explicit worker
framework reference supplies native Generic Host APIs and matches the existing receiver's
ASP.NET shared-framework dependency. No runtime code generation, transport facade, global
transaction/context, leader election, worker registry or deployment engine was introduced.
There is **no new reusable mechanism proven**: native host composition plus the existing
library leases, row locks, fresh scopes and operation contracts removed the need for one.

## New process evidence

[WorkerProcessTests](../../samples/MessagingWorkerDemo.Tests/WorkerProcessTests.cs) starts the
built producer/worker executables as actual child processes against PostgreSQL **18.6** and
RabbitMQ **4.3.6** via the existing Testcontainers fixtures. Each test gets fresh module
databases and a private queue. No external/personal runtime is required.

| Scenario | Observed result |
| --- | --- |
| Explicit setup | Processing on an unconfigured database exits unsuccessfully without creating its schema. Two setup runs are idempotent for the same definition; subsequent worker startup seeds no business/delivery rows. |
| API-independent delivery and restart | API submits and exits before transport workers start. Intake acknowledges retained work while business processing remains pending. A separately started processor saves the correct ID/page count. Fresh processes then handle a second newly submitted request. |
| Competing processors | Two processes reach separate job inserts while holding distinct inbox rows. Both commit one local job/completion after release; `SKIP LOCKED` avoids waiting on the first delivery. |
| Abrupt processing death | Terminate a process with a job already inserted but uncommitted. Release the barrier, observe its backend disappear and verify no committed job/completion. A replacement handles the retained delivery. |
| Graceful in-flight stopping | Hold that same uncommitted insert, send SIGTERM and keep the barrier closed. Native cancellation exits successfully and rolls back. A fresh process handles the delivery after release. |
| Confirmed publication death | Block database completion after broker confirmation, then terminate the dispatcher. Retained intake/local processing succeeds; outbox completion rolls back, leaving its committed claim. Observe actual database-clock lease expiry and start a replacement. It republishes the same MessageId; intake returns AlreadyReceived, with one retained delivery and one job, and the second claim completes. |

[DatabaseBarrier](../../samples/MessagingWorkerDemo.Tests/DatabaseBarrier.cs) uses test-only
triggers in disposable databases: AFTER INSERT for uncommitted jobs and BEFORE UPDATE for
outbox completion. `pg_locks`/`pg_stat_activity` establish in-flight work. These are not
consumer migrations, product fault switches or arbitrarily timed kill/sleep tests. Fresh
contexts observe committed results; the lease is never rewritten to force expiry.

The five-scenario native MTP run passed in **65.1 seconds**. The initial publication assertion
had used the wrong enum label (`Received` instead of `Queued`); correcting the test resolved
its timeout. After strengthening the insert barrier and adding in-flight graceful stopping,
the three affected processor scenarios passed through the installed xUnit executable in
**36.4 seconds**. All six scenarios are therefore verified; the unchanged setup, independent
delivery/restart and confirmed-publication scenarios use the preceding full-run results.
The timings are local observations, including infrastructure startup, not hosted CI or
throughput guarantees. This is distinct from historical O1/I1 connection-loss/context tests.

Reproduce all six with:

```bash
dotnet test --project samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj
```

The focused final invocation was:

```bash
dotnet samples/MessagingWorkerDemo.Tests/bin/Debug/net10.0/MessagingWorkerDemo.Tests.dll \
  -noColor -reporter verbose -method '*KilledProcessor*' \
  -method '*CompetingProcessing*' -method '*GracefulStop*'
```

The process harness is Linux/POSIX-specific for SIGTERM and bounds proof/cleanup deadlines.
The host itself uses ordinary .NET lifecycle APIs. Supported providers remain PostgreSQL;
these proofs do not establish external exactly-once effects or arbitrary provider behavior.

## Existing regression verification

Fresh checks on this branch, separate from historical report results:

| Check | Result |
| --- | --- |
| Messaging contract tests | 7 passed |
| Outbox PostgreSQL tests | 28 passed |
| Inbox PostgreSQL tests | 28 passed |
| Independent HTTP outbox consumer | 5 passed |
| Inbox/RabbitMQ consumer | 10 passed |
| Existing Inventory dispatch adoption (`*DispatchTests`) | 17 passed |
| Architecture | 79 passed, including new host/module dependency boundaries |
| Entire active solution build | Passed, zero warnings/errors; final strengthened proof project also built cleanly |
| Solution semantic style/analyzers | Passed; affected proof files checked again after strengthening |
| CSharpier | 544 active files passed; changed proof files checked again |
| Frozen archive integrity | All 800 original files verified |
| CI metadata | Workflow/filter YAML parsed; three new roots matched Messaging, excluded from unmapped/runtime and excluded on Markdown changes in a native-glob sanity check |

Full Wholesale browser/Aspire deployment, generated-template
adoption and unrelated family runtime suites were not rerun for this native-host composition.
The complete active solution build includes their projects. No hosted CI run is claimed.

## Review map and remaining gaps

- Producer and worker directories: executable composition and sample transport/lifecycle policy.
- Worker test directory: six actual-process proofs, bounded child lifecycle and SQL barriers.
- `samples/OutboxDemo/ExportDbContext.cs`: existing configuration shared with the new host.
- Architecture tests/solution: three new projects and module/host independence checks.
- CI family command/path map: process suite belongs to Messaging; future edits to these
  sample roots do not select unrelated sample runtime. This PR's project/CI metadata changes
  deliberately count as shared inputs under the existing all-check policy.
- Family/sample READMEs, current roadmap/plan and this report: setup, supported behavior,
  extraction findings and deferred work. No template preset is added.

Archive, existing migrations/fixtures, event checkpoints and unrelated worktree edits are
preserved. The index remains the owner's review snapshot (empty here).

Each worker remains sequential with one queue/subscription in this sample. Replica counts
can differ by role, but no sustained-load benchmark, fairness/FIFO guarantee, leader election,
partition assignment, batching or bounded parallelism is claimed. Processing covers bounded
local database work; external effects must go through outgoing work. The consumer owns
business idempotency, admission, queue retention, permissions/TLS and deployment resource limits.

Reconnect, transport health supervision, poison/dead-letter/redrive policy, retention windows
and lease renewal remain later M2/deployment choices. Native BasicGet polling is an explicit
small sample, not a high-throughput consumer prescription. W3C context/spans remain OBS1;
snapshot/feed repopulation, workflow orchestration, aggregate maintenance and template adoption
retain their separate roadmap scopes. No library extraction or template change is needed to
complete W1's demonstrated hosting capability.
