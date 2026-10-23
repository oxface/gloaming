# Completed-outbox retention

Delete one finite batch explicitly, independently of dispatch or dead-letter registration:

```csharp
await using var scope = maintenanceServices.CreateAsyncScope();
var database = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
int deleted = await database.DeleteCompletedPostgresOutboxBatchAsync(
    "exports.render", completedBefore, batchSize: 100, cancellationToken);
```

Reference the PostgreSQL package/namespace. Consumer code authorizes the exact nonblank route,
maintenance credential and evidence window. BatchSize is 1–1000. CompletedBefore accepts a
DateTimeOffset, normalizes it to UTC and rejects a cutoff later than the database time checked
at operation start. There is no default retention duration or automatic scheduler.

## Selection and concurrent execution

The fresh context must have no tracked entities or native/enlisted/ambient transaction.
The operation owns a short ReadCommitted transaction. After checking the cutoff against the
database clock, a materialized candidate SELECT chooses at most BatchSize identities ordered
by dispatched_at and PostgreSQL UUID order. Selection and final DELETE require the exact route,
dispatched_at strictly before the cutoff, no dead letter and no lease token/expiry. Pending,
delayed, leased, dead-lettered, boundary-time and other-route work are preserved.

The candidate SELECT acquires no row locks; native DELETE acquires locks and rechecks final
predicates. This requires no UPDATE privilege. Competing cleaners can select the same rows,
wait, and delete fewer or zero after another commits. Zero does not prove exhaustion. A fixed
candidate batch does not refill after concurrent changes. Database lock waits, deadlocks, SQL
faults, cancellation and command timeouts retain native behavior; consumers configure deadlines
and any whole-call retry. There is no SKIP LOCKED, fairness or latency guarantee.

The returned count means deletion commit was observed. Errors propagate without compensation
or hidden retries. A missing commit response can leave deletion committed. Repeating the same
route/cutoff may delete another batch; it is not an idempotent batch identity or a durable total.
Inspect fresh storage and decide repetition/reporting in consumer code. Cleanup never publishes
or updates inbox data, claims, failure counts or retry eligibility.

## Identity history and authority

Deleting removes the sender's envelope, identity constraint and failure/redrive evidence for
those rows. Keep message identities unique after cleanup; there is no permanent tombstone
registry. A producer can insert a previously deleted MessageId again. Completed state also
does not prove every obsolete publisher has stopped: a late publisher may still accept
externally after deletion, although its old token cannot complete/account or recreate a row.
Receiver deduplication and semantic idempotency remain necessary. Choose the evidence window
and inbox deduplication lifetime independently; cleanup is not completed-message replay.

Native grants/RLS govern visible/deletable rows; EF query filters are not applied. Minimum
privileges are schema USAGE, table DELETE and SELECT on message_id, destination, dispatched_at,
dead_lettered_at, lease_token and lease_until. No payload SELECT, INSERT or UPDATE is required.
Dispatch credentials retain their existing grants and cannot delete. Use separately admitted
maintenance authority instead of expanding dispatcher or HTTP operator permissions.

A DELETE grant is table-wide and cannot itself enforce the legal lifecycle predicate against
arbitrary SQL. Trusted application admission and any native row-security restriction remain
consumer obligations. RLS can restrict SELECT/DELETE to an admitted tenant and completed state;
owners must review effects on every producer/dispatcher role when enabling it. The library does
not install roles, change policies, bypass RLS or reconcile grants.

## Consumer adoption and schema

[OutboxDemo](../tests/Fixtures/OutboxHost/README.md#completed-outbox-cleanup) provides a separate
`--cleanup` role. Required maintenance connection, UTC-Z cutoff and batch-size environment
settings produce one batch and one JSON result. The command verifies its actual maintenance
login/effective privileges, rejects owner/overprivileged credentials, requests a thirty-second
cancellation deadline and starts no workers, telemetry sampler, receiver, HTTP host or migrations.
The check is bounded sample role policy, not a general database-security certification.

Exports opts into a partial `(destination, dispatched_at, message_id)` index where dispatched_at
is not null. Its forward AddCompletedOutboxRetentionIndex migration changes no rows or existing
constraints. Apply consumer migrations explicitly with `--setup` before cleanup. The index is
sample-owned rather than mandatory library mapping; other contexts choose their own index and
deployment plan. Its ordinary transactional index build is not an online/concurrent-index
rollout guarantee. Batch row limits do not bound scan cost, lock time, WAL, disk reclamation or
vacuum behavior; this slice supplies no capacity benchmark or automatic maintenance service.

See [the scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/outbox-completed-retention.md) and
[new proof report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/outbox-completed-retention.md) for actual PostgreSQL,
credential, migration and child-process evidence. [Dead-letter retry](outbox-recovery.md) and
[backlog/worker health](outbox-observability.md) retain their separate contracts. Inbox [processed-receipt expiry](inbox-retention.md),
payload editing, completed-message replay, lease renewal, global execution caps and durable
cleanup scheduling/accounting remain separate capabilities.
