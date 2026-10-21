# Bounded completed-outbox retention

Date: 2026-10-21. Owner-selected implementation after approving visibility checkpoint
`2125860`, on `feat/outbox-completed-retention`. The [scope](../plans/outbox-completed-retention.md)
records concrete usage, authority and schema boundaries. This report distinguishes new
retention proofs from the earlier [visibility](outbox-operational-visibility.md),
[completion](m2-outbox-completion-supervision.md) and [dead-letter](m2-outbox-dead-letters.md) evidence.

## Outcome and extraction findings

DeleteCompletedPostgresOutboxBatchAsync is a separately authorized callable PostgreSQL context
extension. An exact route, explicit nonfuture completion cutoff and batch size 1–1000 select
one bounded deletion. The return value is the observed committed count. Pending, delayed,
leased, dead-lettered, boundary-time and other-route work is preserved.

The new reusable mechanism is a finite native cleanup primitive. Existing model/identifier,
parameter and fresh-context internals are reused because their semantics match. No recovery
interface, generic queue, scheduler, tombstone registry or authority framework was proven or
added. Consumer code retains admission, evidence windows, native grants/RLS, repetition, audit
and reconciliation. Inbox deduplication lifetime remains independent. Template output is unchanged.

The candidate SELECT takes no row locks; the final DELETE repeats eligibility and uses ordinary
native locking/rechecks. This permits metadata-only SELECT plus DELETE without UPDATE. Concurrent
cleaners can wait and return fewer or zero after a winner commits. Zero is not an exhaustion
guarantee; fixed candidates are not refilled. Native waits/deadlocks/SQL/cancellation/commit errors
propagate without hidden retries or compensating writes. A lost response can leave deletion
committed, and repeating the route/cutoff can remove another batch rather than identify the
same logical batch. No global durable deletion total or latency guarantee is supplied.

Deleting removes sender identity/envelope/failure evidence and the retained-row uniqueness
constraint. A producer can insert that MessageId again, so consumers must preserve identity
uniqueness beyond cleanup. Completed state does not imply obsolete publishers have stopped;
receiver deduplication and semantic idempotency remain necessary.

## Consumer, authority and schema scope

[OutboxDemo](../../samples/OutboxDemo/README.md#completed-outbox-cleanup) has a separate finite
`--cleanup` role. Required maintenance connection, UTC-Z cutoff and batch-size settings run one
fixed exports.render batch and print only route/cutoff/batch/count after observed commit.
It requests a thirty-second cancellation deadline and starts no producer, receiver, worker,
HTTP host, sampler or migrations. Owner-provisioned maintenance credentials receive six-column
metadata SELECT and DELETE, with no UPDATE, payload read, ownership or business access.

The executable checks actual login/effective privileges, including PUBLIC effects, before
deletion. Owner credentials and expanded payload, PUBLIC update or business grants reject.
This is consumer role policy, not a generic database-security certification. Table DELETE does
not itself enforce lifecycle predicates against arbitrary SQL; native RLS and trusted caller
admission remain independent obligations. A separate RLS proof restricts direct deletion to
admitted completed rows. Existing Sales dispatcher grants are unchanged and cannot delete.

Exports explicitly adds partial `(destination, dispatched_at, message_id)` index
`ix_exports_outbox_completed_retention`. Its forward
[migration](../../samples/OutboxDemo/Migrations/20261021125631_AddCompletedOutboxRetentionIndex.cs)
and four-line snapshot addition preserve rows and existing constraints. Historical migrations
and archived source are unchanged. The library imposes no index or migration on other contexts;
consumers choose their own rollout. The ordinary index build is not a concurrent/online rollout
promise, and a batch limit does not bound scan/WAL/vacuum cost or lock time.

## New proofs and verification

Real PostgreSQL 18.6 and public callable/mapped-row boundaries establish:

- Strict cutoff, exact route and batch limits; deterministic PostgreSQL UUID order for equal
  completion times; UTC normalization, nonfuture cutoff and invalid-batch rejection. Explicit
  delayed and both live/expired lease fixtures remain retained. Caller-owned transactions reject.
- Repetition stays bounded and can remove later batches. Reusing a deleted MessageId is accepted
  by the sender database, explicitly proving that no permanent identity fence survives deletion.
- Native deferred commit rejection rolls back actual deletion. An EF interceptor throwing only
  after real PostgreSQL commit leaves deletion applied; no compensation or fabricated count occurs.
- Two cleaners select the same candidates and the second is observed blocked through
  pg_blocking_pids. The first removes two, the waiter removes zero, and a fresh call removes the
  remaining eligible row. Unrelated dispatch completes while the cleaner holds a delete lock;
  its new completion remains beyond the fixed old cutoff.
- Cancellation is sent only after native blocking is observed; rollback preserves work and a
  fresh context cleans it. A built child is killed while its DELETE is held by an advisory trigger;
  after backend exit, old rows remain and a fresh child removes exactly one batch.
- An obsolete publisher is released after successor completion and cleanup. Its external acceptance
  can still occur; successful return yields ClaimLost, and an exception retains the original error.
  Neither completion nor accounting recreates the deleted row.
- Six-column SELECT/DELETE credentials execute cleanup with no UPDATE. Native RLS limits the
  admitted tenant/completed state; actual UPDATE, payload reads and business reads are denied,
  while a direct pending-row delete is excluded by RLS. The existing restricted Sales dispatcher
  receives InsufficientPrivilege when invoking retention, leaving its work untouched.
- The forward Exports index migration preserves independently captured existing JSON rows,
  matches native index metadata and the current EF model, then supports bounded cleanup.

Fresh successful suite runs, with zero failures/skips:

| Suite | Passed |
| --- | ---: |
| OutboxPostgresTests, complete | 102 |
| OutboxDemo.Tests, complete | 19 |
| MessagingWorkerDemo.Tests, complete | 18 |
| WorkflowDemo.Tests, SalesDispatchTests | 36 |
| ArchitectureTests, complete | 96 |
| **Total** | **271** |

Twelve provider cases, five executable cases, one migration case and one dispatch-denial case
are new. Other results are fresh regressions, including real RabbitMQ/OTLP, lease fencing,
operator retry, native health/gauges and failure-accounting/completion supervision. The provider
retention cases also rerun after making delayed/expired-lease preservation explicit.

The active solution builds with zero warnings/errors. Full solution style/analyzer checks,
CSharpier (899 files including generated source), git diff --check and final Markdown/local-link/
whitespace checks pass. Development failures exposed raw fixture
JSON braces interpreted by EF formatting, a missing explicit Attempts fixture value, an
incorrectly named business column in a denial assertion, and generated migration namespace/
array style. These were corrected; failed runs are not counted as passing evidence.

Reproduce from the repository root with the configured Docker-compatible runtime:

```bash
dotnet build ModulithFoundry.slnx --no-restore
dotnet test --project src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj --no-build --no-restore
dotnet test --project samples/OutboxDemo.Tests/OutboxDemo.Tests.csproj --no-build --no-restore
dotnet test --project samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/WorkflowDemo.Tests/WorkflowDemo.Tests.csproj --no-build --no-restore -- --filter-class '*SalesDispatchTests'
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj --no-build --no-restore
dotnet format style ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet format analyzers ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet csharpier check . --include-generated
git diff --check
```

## Review files and remaining limits

Review [the callable SQL](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxRetentionExtensions.cs),
[consumer authority](../../samples/OutboxDemo/ExportOutboxRetention.cs), Program role selection,
the index/migration, [PostgreSQL proofs](../../src/Gloaming.Messaging/tests/OutboxPostgresTests/RetentionTests.cs),
[process proofs](../../samples/MessagingWorkerDemo.Tests/OutboxRetentionProcessTests.cs) and
[family contract](../../src/Gloaming.Messaging/docs/outbox-retention.md) first.

Native RLS/grants and trusted consumer maintenance admission remain essential. No retention
duration, global execution cap, cleanup scheduler, durable cleanup ledger, online index rollout
or capacity benchmark is selected. Inbox retention, queued-schema evolution and lease renewal
remain separate; payload editing and completed-message replay remain excluded. This work stays
unstaged/uncommitted and the originally empty index is preserved. No push or pull request was requested.
