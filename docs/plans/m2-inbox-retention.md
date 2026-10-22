# M2: bounded completed-inbox retention and deduplication expiry

Status: **interfaces and implementation scope owner-approved**, 2026-10-22, through
“proceed with implementation.” Implementation and executable proofs are prepared for review;
see the [slice report](../reports/m2-inbox-retention.md). M2 closure remains pending exact
change-set review and explicit owner dispositions for the remaining v1 gaps below.

## Outcome and existing behavior

The owner includes inbox retention/deduplication expiry in M2 before v1. Earlier completed
sub-slices do not close that block. The current inbox retains all receipts indefinitely;
its primary key `(SubscriptionKey, ProducerKey, MessageId)` supplies delivery deduplication
while a row remains. Cleanup must preserve pending, delayed, actively processed,
dead-lettered and operator-retried-but-not-yet-processed work.

Read together with the [current-code assessment](../reports/m2-inbox-retention-assessment.md),
[inbox contract](../../src/Gloaming.Messaging/docs/inbox.md),
[dead-letter contract](../../src/Gloaming.Messaging/docs/inbox-recovery.md) and
[completed-outbox cleanup](../../src/Gloaming.Messaging/docs/outbox-retention.md).
The outbox's completed state is sender publication bookkeeping. An inbox's processed receipt
is also the receiver's duplicate and incompatible-content evidence; deleting it changes the
receiver's acceptance contract.

## Proposed callable interface

One PostgreSQL context extension, independent of intake/handler/worker/dead-letter registration:

```csharp
public static class PostgresInboxRetentionExtensions
{
    public static Task<int> DeleteProcessedPostgresInboxBatchAsync<TDbContext>(
        this TDbContext database,
        string subscriptionKey,
        string producerKey,
        DateTimeOffset processedBefore,
        int batchSize,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext;
}
```

- Require an exact nonblank subscription and producer; no wildcard or all-lanes mode.
- Admit **only** `processed_at IS NOT NULL AND quarantined_at IS NULL`, with
  `processed_at < processedBefore`. Completion includes normal handler return after a
  consumer-handled business refusal; it does not mean a positive business outcome.
- Pending, processing, delayed, dead-lettered and newly redriven receipts remain, however old
  their receipt/failure timestamps. No age-based deletion of unprocessed work.
- Normalize the explicit cutoff to UTC and reject a cutoff later than the database clock
  checked at operation start. Equality is excluded. There is no default duration.
- Require `batchSize` from **1 through 1000**, with no implicit default. This is a finite row
  bound for an administrative operation, not a scan-cost, lock-time or storage-reclamation
  bound. The caller can choose a smaller batch.
- Require a fresh context without tracked entities or native/ambient/enlisted transactions.
  Own one short native ReadCommitted transaction. Return a deletion count only after the
  commit response is observed. Native errors/cancellation propagate; no hidden retry.
- A lost commit response can leave deletion committed. A repeated call can delete the next
  batch; it is neither an idempotent batch identity nor a durable cumulative count.

The proposed SQL uses a materialized candidate selection ordered by processed time and
PostgreSQL UUID order, bounded by the batch size, in the exact subscription/producer lane.
Candidates carry the full delivery key **and receipt generation**. Final DELETE matches all
of those values and repeats the lifecycle/cutoff predicates. Native DELETE locking can wait;
selection uses no `FOR UPDATE`/`SKIP LOCKED`, so maintenance needs no UPDATE grant. Concurrent
cleaners may select the same batch and return fewer rows or zero. They do not refill that
selection. Zero is not a global exhaustion guarantee. RLS applies; EF filters do not.

No `IInboxRetention`, shared inbox/outbox recovery interface, scheduler or generic queue
framework is proposed. Keep outbox cleanup unchanged. Share an internal helper only if the
implementation establishes identical behavior worth sharing; similarly shaped SQL is not
sufficient reason to consolidate their lifecycle policies.

## Deduplication guarantee and intake race

**An old delivery can be accepted and its handler can execute again after its processed
receipt is deleted.** Expiry is actual committed deletion, not merely passing the cutoff.
Until deletion, equivalent intake still returns AlreadyReceived and incompatible envelope
reuse conflicts, including while the receipt is completed or dead-lettered.

After deletion there is no tombstone, original envelope comparison or receipt accounting to
consult. The same delivery key can return Queued with a new receipt generation, fresh receipt
time and zero receipt-local failure/redrive counts. Current adapter admission, decoding and
business rules still apply. A previously incompatible envelope may also be accepted once its
comparison evidence is removed. Delivery IDs must stay stable on redelivery; assigning a new
ID cannot extend deduplication.

Consumer policy must choose a window accounting for broker redelivery, operator retry,
sender recovery/stale publishers, outages and producer schema support. Neither sender cleanup
nor broker acknowledgement establishes a safe universal window. Consumer semantic operation
IDs, constraints and retained business outcomes can prevent repeated business effects beyond
receipt expiry. Those records are not deleted by this interface. The sample's RenderJob key
can demonstrate that distinction; an independent handler proof must also show actual second
execution/effect when no semantic guard exists. Do not claim permanent deduplication from the
sample's business constraint.

Current ReceiveAsync executes INSERT ON CONFLICT DO NOTHING, then a separate envelope SELECT.
A deleted winner between the statements currently looks like a content conflict. Proposed
correction: when comparison finds **no row**, repeat insert/compare inside the same caller-owned
ReadCommitted transaction; a present incompatible row still throws. Implementation limits this
to three insert/compare observations, then throws DbUpdateConcurrencyException without acceptance.
This prevents unbounded work if native RLS hides the conflicting key; it does not refresh
operator tokens, bypass SELECT authority or repeat business handling. A successful duplicate
comparison observes a retained receipt at that statement's snapshot. Cleanup can commit
after that observation; intake takes no retention pin through acknowledgement. A later
arrival after deletion can therefore queue again. Preserve explicit caller save/commit and
native cancellation/timeouts. No new intake UPDATE grant or no-op envelope UPDATE is needed.

## Receipt generation and existing operator retry

Retention exposes a second correctness gap: Revision starts at zero for a newly inserted
receipt. An old request for revision 1 can match a later dead-lettered receipt with the same
delivery key and revision after the earlier receipt was retried, processed and deleted.
Revision alone cannot preserve operator authority across expiry.

Proposed addition: immutable database-generated nonempty **ReceiptId**, a UUID identifying
one retained receipt generation. Keep the existing delivery primary key, wire MessageId,
payload, schema, original receipt time and revision meaning. A duplicate does not replace
ReceiptId; operator retry preserves it. A new insertion after deletion gets a new ReceiptId.
It is an optimistic concurrency token, not an authority credential or a new delivery key.

```csharp
// Add to InboxMessageRecord and detached InboxDeadLetter metadata:
public Guid ReceiptId { get; } // private setter on the EF record

// Revise the existing interface; List/Get and InboxRetryResult remain unchanged.
Task<InboxRetryResult> RetryAsync(
    string subscriptionKey,
    string producerKey,
    Guid messageId,
    Guid expectedReceiptId,
    long expectedRevision,
    CancellationToken cancellationToken = default);
```

Reject an empty expectedReceiptId. The conditional UPDATE must match ReceiptId **and** revision,
the full delivery key and current unprocessed dead-letter state. Old-generation retry returns
NotApplied even if the new generation has reached the same revision. Two operators with the
same current tokens still apply at most once. Do not retain an unsafe revision-only overload.
This is an explicit pre-v1 interface/HTTP request change, not a duplicate recovery interface.

Rendering's existing authenticated retry body becomes:

```json
{ "expectedReceiptId": "the-receipt-uuid-returned-by-Get", "expectedRevision": 1 }
```

All existing inbox retry callers and examples must pass metadata from their observation:

```csharp
var observed = await deadLetters.GetAsync(subscription, producer, messageId, cancellation);
if (observed is not null)
    await deadLetters.RetryAsync(subscription, producer, messageId,
        observed.ReceiptId, observed.Revision, cancellation);
```

## Concrete consumer adoption and authority

Rendering owns the demonstrated policy: a separate finite `InboxDemo --cleanup` mode with
`INBOX_DEMO_MAINTENANCE_CONNECTION_STRING`, `INBOX_DEMO_PROCESSED_BEFORE` (explicit UTC-Z
timestamp), and `INBOX_DEMO_CLEANUP_BATCH_SIZE`. The command authorizes its fixed lane
`rendering.jobs` / `exports`, verifies a dedicated metadata-only maintenance login, requests
a thirty-second cancellation deadline, runs **one** batch and prints one JSON result with
the lane, cutoff, batch size and observed deletion count. No API, intake, processor, migration
or telemetry loop starts. Setup remains an explicit separate operation; scheduling and
repetition belong to the consumer, so no additional permanent worker is needed.

The underlying consumer call is:

```csharp
await using var database = RenderDbContext.Create(maintenanceConnection);
int deleted = await database.DeleteProcessedPostgresInboxBatchAsync(
    "rendering.jobs", "exports", processedBefore, batchSize: 100, cancellationToken);
```

Minimum native privileges are schema USAGE, table DELETE and SELECT on subscription_key,
producer_key, message_id, receipt_id, processed_at and quarantined_at. No payload, business
table, INSERT or UPDATE authority is needed. Intake/processing/operator credentials gain
**no DELETE**. Inspection/retry may need explicit receipt_id SELECT in a column-restricted
installation; existing full-table inbox SELECT already covers it. Receipt generation uses
PostgreSQL's native UUID default and needs no sequence grant.

A table DELETE grant does not prevent arbitrary SQL issued by its holder from deleting
unprocessed rows. The library predicates are a trusted-call contract, not a SQL sandbox.
The finite sample rejects owner/superuser/BYPASSRLS and expanded effective privileges, as
consumer role policy. An independent restricted-role RLS proof must demonstrate completed-only
lane/tenant visibility and deletion with native policies. No library policy installation,
RLS bypass, automatic role reconciliation or global change to existing consumer policies.

## File, schema and deployment scope after approval

| Area | Concrete scope |
| --- | --- |
| Existing EF interface/metadata | InboxMessageRecord, InboxDeadLetter and IInboxDeadLetters: receipt generation and required conditional retry input; receipt-local counter documentation. |
| PostgreSQL implementation | New PostgresInboxRetentionExtensions; mapping/storage validation for receipt_id; narrow missing-comparison-row correction in PostgresInbox; generation-aware PostgresInboxDeadLetters predicate/projection. Processor row-lock/savepoint protocol remains. |
| Current consumers | Rendering operator body/logging and every inbox retry caller/test/example, including WorkerTopology and restricted Sales. No outbox retry interface changes. |
| Consumer schema | Forward Rendering, Inventory and Sales migrations add nonnull UUID receipt_id with gen_random_uuid() default and a nonempty constraint, backfilling existing receipts without changing envelope/key/lifecycle/counters. Retain all earlier migrations. Update their snapshots. |
| Cleanup index | Rendering opts into a partial `(subscription_key, producer_key, processed_at, message_id)` index for processed, non-dead-lettered receipts. Other contexts select an index when adopting cleanup; no mandatory cleanup index in library mapping. |
| Executable adoption | New RenderInboxRetention plus InboxDemo CLI branch; sample-owned grants/invocation instructions. No automatic cleanup in the API or processor. |
| Proofs | InboxPostgresTests retention/intake/dead-letter cases; InboxDemo.Tests migration/HTTP/adoption; MessagingWorkerDemo.Tests finite command/death/restart; restricted tenant/role proof and impacted consumer regressions. |
| Documentation | Messaging README, EF/provider READMEs and inbox/recovery/compatibility/capabilities guides; new family inbox-retention guide; sample READMEs; design/extraction/roadmap and final M2 slice/closure report. |

Rollout: apply additive schema migrations and explicit column grants; upgrade **all** intake
and operator binaries/callers; only then authorize cleanup. Old retry binaries ignore the
new generation and old intake binaries retain the false-conflict race, so mixed operation
while cleanup is active is unsupported. Migration alone cannot coordinate the fleet.
Downgrading to those binaries requires stopping cleanup and an explicit compatibility review;
stopping future cleanup cannot restore deleted duplicate evidence. No automatic down migration
or rollout controller is proposed. Record the settled generation choice in an ADR after review.

## Required fresh PostgreSQL proofs after interface approval

| Obligation | Observable proof |
| --- | --- |
| Eligible lifecycle and scope | Exact subscription/producer, strict processed cutoff (including equality/UTC offset), batch bounds and deterministic UUID ties. Preserve old pending/delayed/dead-lettered/retried rows, other lanes and local business/outbox state. |
| Competing cleaners | Pause real DELETE, observe a second backend waiting, release and assert bounded committed counts and surviving rows. Repeated cleanup reaches zero on a stable eligible set; zero during a race is not asserted to mean exhaustion. |
| Intake versus cleanup | Gate the gap after a conflicting INSERT but before comparison using disposable-database triggers/advisory locks. Delete the completed winner; assert Queued via reinsert, not a false conflict. Also observe intake waiting on an uncommitted DELETE: committed deletion allows Queued; rollback retains AlreadyReceived or true content conflict. |
| Processing versus cleanup | Hold a handler's native row lock and uncommitted completion, run cleanup and prove no loss of active receipt/effects. Commit and roll back in separate cases; a later eligible cleanup removes only committed processed work. Other eligible rows can still be cleaned. |
| Operator retry versus cleanup | Race revision/generation-conditional retry with cleanup while a row is dead-lettered and after retry makes it pending. Cleanup never removes either state; no stale request reopens completed work. |
| Generation reuse | Dead-letter, observe tokens, retry, process, delete, redeliver the same key and dead-letter at the **same revision** in the new generation. Old token returns NotApplied; current tokens race once successfully; repeated old/current requests remain conditional. Preserve envelope/counters within each generation. |
| Redelivery semantics | Before cutoff and after cutoff but before deletion, equivalent delivery deduplicates. After deletion, same identity queues and an independent non-idempotent local handler executes again. Separately prove Rendering retains one job through its own business key. Changed envelopes conflict only while retained; demonstrate acceptance after evidence removal subject to current admission. |
| Rollback and ambiguity | Real SQL/commit rejection rolls back deletions. Native successful commit followed by simulated response loss leaves deletion applied. Fresh calls observe state and remain bounded. No compensation or durable-total claim. |
| Authority | Metadata-only DELETE role succeeds without payload/business/INSERT/UPDATE access; intake/processor/operator roles cannot delete. Native RLS limits cleanup to admitted completed rows/lane/tenant; EF-filter bypass does not bypass RLS. Restricting SELECT on receipt_id makes the requirement visible. |
| Migration/rollout | Upgrade old pending/completed/dead-lettered receipts in all three migrated consumers: unique nonempty receipt tokens, same raw envelope, key and recovery state. Current model has no pending changes. Missing/empty HTTP retry token rejects without mutation. |
| Finite command/restart | Run the actual built --cleanup child process with restricted credentials. Reject unsafe credentials before deletion; prove one-batch exit. Kill while DELETE is blocked, await backend rollback, restart and verify original evidence survived until a successful cleanup. |

Use existing native PostgreSQL fixtures and observable backend locks, rather than sleeps
as race evidence. No production fault switch or generic proof framework is needed. Run a
whole-solution build, the Messaging CI suites and impacted Serialization/Architecture checks,
style/analyzers/CSharpier and whitespace/link checks. Broaden only for affected failures.
The final report must list actual fresh results separately from earlier M2 evidence.

## M2 closure review and requested dispositions

The [assessment](../reports/m2-inbox-retention-assessment.md) records current-code findings.
Do not declare M2 complete merely because this proposal or a previous sub-slice is complete.

| Remaining concern | Proposed v1 disposition |
| --- | --- |
| Processed inbox growth / explicit duplicate-evidence lifetime | Implement this cleanup contract and document actual expiry and re-execution. |
| Intake missing-winner race and old operator authority matching a new receipt | Resolve in this same slice, with deterministic PostgreSQL proofs and all active caller/schema updates. |
| Recovery, failure accounting, inspection, conditional retry, supervision, restricted outbox dispatch, completed-outbox cleanup and chosen schema evolution | Already implemented in prior M2 work. Reconcile current docs and run affected regressions; cite earlier proofs as historical, not new guarantees. |
| Dead-lettered/unprocessed storage growth and terminal disposition | **Request explicit post-v1 deferral** of automatic expiry, discard and terminal purge. Preserve these rows indefinitely; the v1 resolution path remains repair plus authorized retry. No unsupported manual-SQL purge recommendation. |
| Deduplication beyond deleted receipts | **Request explicit post-v1 deferral** of persistent tombstones/a permanent inbox duplicate registry. v1 promises suppression only while evidence is retained; long-lived semantic idempotency stays consumer-owned. If permanent delivery suppression is required, this deletion contract must be reconsidered before implementation. |
| Automatic maintenance schedules, durable cumulative cleanup accounting, operator console and inbox dashboards/alerts | **Request explicit post-v1 deferral** of new supplied mechanisms. Finite executable maintenance, metadata inspection and native consumer hosting/logging remain the v1 adoption path. |
| Crash-inclusive execution caps, external exactly-once effects, payload editing and completed-message replay | **Request explicit exclusion from v1 M2 guarantees**. Recorded failure counts remain observations; outgoing publication retains uncertainty. A user wishing to pursue these must select a separate concrete policy/proof rather than infer support. |

Lease renewal, dispatch pools, ordering, additional providers/transports and unrelated template
or projection work are not added by this request. Existing supported transports and lease
fencing must remain intact. Consumer duration/scheduling and fleet deployment policy are
documented obligations, not unspecified library implementations to close later.

The owner approved the cleanup interface **and** the ReceiptId retry/schema change before implementation.
The post-v1 dispositions above require explicit owner agreement; implementation approval
alone is not recorded as that agreement. Final M2 closure also requires the executable proofs,
family documentation, report and review of the exact complete change set before any commit.
