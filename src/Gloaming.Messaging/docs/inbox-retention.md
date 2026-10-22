# Processed-inbox retention and deduplication expiry

Delete one finite batch through the PostgreSQL package, independently of intake, processing
and dead-letter registration:

```csharp
await using var database = RenderDbContext.Create(maintenanceConnection);
int deleted = await database.DeleteProcessedPostgresInboxBatchAsync(
    "rendering.jobs", "exports", processedBefore, batchSize: 100, cancellationToken);
```

Authorize the exact subscription/producer lane and maintenance credentials in consumer code.
The explicit DateTimeOffset cutoff normalizes to UTC and must not be later than the database
clock checked at operation start. Batch size is 1–1000. No retention duration or scheduler
is supplied. Keep maintenance separate from intake, processing and operator retry authority.

## Eligible states and concurrency

Only receipts with `processed_at < processedBefore AND quarantined_at IS NULL` qualify.
Processed means the local handler returned normally and its transaction completed, including
a handled business refusal. It does not mean a positive business outcome. Pending, delayed,
actively processed, dead-lettered and retried-but-unprocessed work is preserved regardless
of receipt age. The existing storage name `quarantined_at` maps to DeadLetteredAt.

Require a fresh context without tracked entities or native/ambient/enlisted transactions.
The operation owns a short ReadCommitted transaction. A materialized selection chooses at most
the batch size, ordered by processed time and PostgreSQL UUID order, in the exact lane.
Final DELETE matches the full delivery key and ReceiptId and rechecks lifecycle/cutoff.
There is no processing lease predicate: inbox processing retains its native row lock and
transaction through local work and completion.

Selection takes no row locks; DELETE acquires native locks. No UPDATE grant is required.
Competing cleaners can wait on the same candidates and delete fewer rows or zero after another
commits. A fixed candidate batch does not refill. Zero is not a global exhaustion guarantee.
Native errors, lock waits, deadlocks, command timeouts and cancellation propagate. Consumers
choose deadlines and whole-call repetition. Batch size does not bound scan cost, wait time,
WAL, vacuum work or disk reclamation; no throughput or online-maintenance guarantee is supplied.

The returned count confirms that call's deletion commit response was observed. A lost response
can leave deletion committed. Repeating the call may delete another batch; there is no batch
identity, compensating write or durable cumulative deletion counter. After uncertainty, inspect
fresh state and decide repetition/reporting in consumer code. Cleanup never removes local business
effects, outgoing work or another lane's receipts.

## What expiry means

**An old delivery can queue and its handler can execute again after its processed receipt is
deleted.** Passing the cutoff alone does not expire it. While evidence remains, equivalent
intake returns AlreadyReceived and incompatible content conflicts, including completed or
dead-lettered receipts. Actual deletion removes that envelope comparison, failure/redrive
history and identity constraint. There is no permanent tombstone registry.

An admitted arrival after deletion can insert the same delivery key with a new ReceiptId,
database receipt time and zero receipt-local counters. Changed content under that key can
also be admitted once the original evidence is gone. Current producer/tenant admission,
decoding and business rules still apply. Sender IDs must remain stable on redelivery; new IDs
do not extend receiver deduplication. A receipt lifetime is not a global execution budget.

Choose the evidence window for the consumer's broker redelivery, outages, stale publishers,
operator retry and schema compatibility obligations. Sender completion/cleanup and transport
acknowledgement do not establish a universal safe horizon. Keep required v1 schema support
for old deliveries even after cleaning current receipts.

For longer-lived protection against duplicate business effects, retain consumer semantic
operation IDs, unique business keys or outcomes independently of inbox receipt cleanup.
Rendering's RenderJob key keeps one local job after receipt expiry; the independent PostgreSQL
proof deliberately uses a non-idempotent local counter and observes a second effect. Do not
mistake the sample's business constraint for permanent inbox delivery suppression.

Intake handles a receipt disappearing between INSERT ON CONFLICT and envelope comparison by
repeating insert/compare inside the same caller-owned ReadCommitted transaction. A retained
incompatible envelope still conflicts. After **three** insert/compare observations that cannot
observe a conflicting receipt, DbUpdateConcurrencyException propagates without acceptance;
an RLS-hidden key or continuing removal must not create an unlimited loop. A duplicate result
observes retained evidence at the comparison statement snapshot; it does not pin that receipt
until transport acknowledgement. Cancellation, native errors and caller commit ownership remain.

## Operator authority across receipt generations

ReceiptId is an immutable, nonempty, database-generated UUID per retained receipt generation.
It does not change the delivery primary key or wire MessageId and grants no authority.
Duplicate intake and operator retry keep it. A new intake after deletion gets another value.
Revision and failure/redrive counts describe transitions within that generation.

The existing dead-letter metadata includes ReceiptId; retry requires both observed tokens:

```csharp
var item = await deadLetters.GetAsync(subscription, producer, messageId, cancellationToken);
if (item is not null)
    await deadLetters.RetryAsync(subscription, producer, messageId,
        item.ReceiptId, item.Revision, cancellationToken);
```

An old generation cannot retry a later receipt even when their revision values match.
Empty receipt tokens reject; there is no revision-only overload. After response loss reuse
the original ReceiptId/revision pair. Getting new tokens requires a new consumer policy decision.
List/Get/Retry registration, current authentication/authorization and the retained envelope
semantics remain in the [recovery contract](inbox-recovery.md).

## Native authority and migration

Native grants/RLS apply; EF query filters do not. Minimum privileges: schema USAGE, table
DELETE and SELECT on subscription_key, producer_key, message_id, receipt_id, processed_at and
quarantined_at. No payload, business-table, INSERT or UPDATE privilege is required. Intake,
processor and operator credentials gain no DELETE authority.

A DELETE grant is table-wide and cannot enforce lifecycle predicates against arbitrary SQL.
Use trusted application admission and, where required, consumer-owned native SELECT/DELETE
policies restricting tenant/lane and processed state. The independent PostgreSQL proof forces
RLS, uses metadata-only credentials and demonstrates that an unrelated EF filter is not the
maintenance authority. The library installs no policies, roles, bypass or grant reconciler.

Apply forward Rendering, Inventory and Sales receipt-generation migrations before the binary.
They backfill nonnull `receipt_id` with `gen_random_uuid()` and enforce nonempty UUIDs while
preserving existing keys, envelopes and recovery state. This backfills existing rows and takes
native migration locks; no online rollout is certified. Rendering alone opts into a partial
`(subscription_key, producer_key, processed_at, message_id)` processed-row index. Other contexts
choose their own index when adopting cleanup.

Column-restricted inspection/retry roles need explicit receipt_id SELECT. Existing full inbox
SELECT grants cover it. Generation defaults require no new sequence privilege. Upgrade every
intake and operator binary/caller before enabling cleanup: old intake retains the deletion race,
and old retry ignores generation. Mixed operation with cleanup is unsupported. Stopping cleanup
does not restore deleted evidence or establish a safe downgrade. Fleet rollout remains consumer
policy. Consumer-native migrations remain explicit; no worker migrates on startup.

## Executable adoption

[InboxDemo](../tests/Fixtures/InboxHost/README.md#processed-inbox-cleanup) supplies a separate
finite `--cleanup` mode, explicit UTC cutoff/batch and checked maintenance connection. It emits
one JSON result and exits without starting API/intake/processing/migration or telemetry loops.
Its role check rejects owner/superuser/BYPASSRLS and expanded effective privileges as bounded
sample policy, not universal database-security certification. Consumers may invoke it manually
or schedule it using their existing infrastructure; no additional permanent worker is required.

Dead letters remain retained for repair and authorized retry. Discard/purge, permanent duplicate
registries, supplied scheduling, durable cumulative cleanup accounting and console/dashboard
features are not implemented by this contract. Their proposed post-v1 disposition still needs
the owner's explicit decision in the [M2 report](../../../docs/reports/m2-inbox-retention.md).
See [completed-outbox cleanup](outbox-retention.md) for its different sender-history contract.
