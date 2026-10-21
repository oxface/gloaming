# Bounded completed-outbox retention

Status: owner selected implementation on 2026-10-21 after approving visibility checkpoint
`2125860`. This concrete surface and its implementation remain subject to line-by-line review.
The [slice report](../reports/outbox-completed-retention.md) records new proofs, verification
and remaining limits separately from earlier outbox evidence.

## Callable surface and semantics

```csharp
int deleted = await database.DeleteCompletedPostgresOutboxBatchAsync(
    "exports.render", completedBefore, batchSize: 100, cancellationToken);
```

One PostgreSQL context extension, independently callable without dispatch/recovery registration,
returns the observed committed deletion count. Authorize the route, credential and cutoff in
consumer code. The fresh context has no tracked entities or native/enlisted/ambient transaction.
BatchSize is 1–1000. CompletedBefore is normalized to UTC and must not be in the future by the
database clock. A short ReadCommitted transaction checks that cutoff, then selects at most the
batch limit ordered by completion time and UUID and deletes only qualifying rows.

Selection and final deletion require the exact route, dispatched_at strictly before the cutoff,
no dead-letter state and no lease token/expiry. Completion/dead-letter/lease constraints remain
unchanged. Repeat all predicates at the final write. The candidate SELECT takes no row locks;
the DELETE uses ordinary native locking/rechecks. This needs metadata SELECT and DELETE, with
no UPDATE grant. Competing cleaners may select the same candidates, wait and delete fewer or
zero rows after a winner commits. Zero does not prove global exhaustion. Native cancellation/
command timeouts bound waits according to consumer configuration; there is no SKIP LOCKED or
latency guarantee.

SQL, cancellation and commit errors propagate. A missing commit response can leave deletion
committed; no compensation or hidden retry occurs. Repeating the same route/cutoff can delete
the next batch, so repetition is not an idempotent batch identity or a durable accounting total.
Consumers decide repetition, reporting, scheduling and retention windows. Raw SQL respects native
grants/RLS, not EF filters. Cleanup never publishes or changes inbox data.

Deleting removes sender-side identity/envelope/failure history. The consumer must keep message
identities unique after deletion and choose the evidence window. Completed state does not prove
every obsolete publisher has stopped. Receiver deduplication/idempotency remains necessary;
inbox retention is independent. No tombstone registry, payload editing or completed-message
replay is added.

## Authority, adoption and schema scope

OutboxDemo adds a finite `--cleanup` role with required OUTBOX_DEMO_MAINTENANCE_CONNECTION_STRING,
OUTBOX_DEMO_COMPLETED_BEFORE and OUTBOX_DEMO_CLEANUP_BATCH_SIZE. It fixes exports.render, verifies
separate maintenance authority, performs one batch and prints a count. No producer/receiver,
operator HTTP token, worker, sampler or migrations run in this role. Owner-provisioned credentials
have schema USAGE, DELETE and SELECT only on message_id, destination, dispatched_at,
dead_lettered_at, lease_token and lease_until. No UPDATE, payload SELECT or business access.
Dispatch credentials remain unchanged and cannot delete. A DELETE grant alone cannot enforce
legal row selection against arbitrary SQL; native RLS and trusted consumer admission remain
separate obligations. Provisioning and existing security policies stay owner-managed.

The sample explicitly adds a partial `(destination, dispatched_at, message_id)` index for
completed rows, with one forward Exports migration and snapshot. The library adds no mandatory
index or schema change; other adopters select their own index/migration. No automatic retention
worker or generic recovery/maintenance framework is introduced.

## Proof and review scope

Use the public extension/mapped row, actual PostgreSQL locks/triggers/commit boundaries, native
credential grants/RLS and the built cleanup executable. Prove cutoff boundaries, route isolation,
ordering/batch limits, preservation of pending/delayed/leased/dead-letter work, competing cleaners,
unrelated dispatcher progress, stale publishers, cancellation/restart, SQL rollback and response
loss after real commit. Prove metadata-only maintenance authority and rejection of ordinary owner/
dispatch credentials. Exercise the forward index migration with existing rows. Keep existing
fencing, recovery, health and restricted Sales tests as regressions.

Review the extension and SQL first, then finite consumer authority/usage, migration, PostgreSQL/
process tests, current family docs and slice report. Retention windows, schedule, audit, deployment
policy, inbox deduplication lifetime and performance capacity remain consumer decisions.
