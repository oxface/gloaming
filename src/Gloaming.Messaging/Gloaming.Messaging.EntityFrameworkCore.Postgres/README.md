# PostgreSQL transactional messaging

References the Messaging EF layer and native Npgsql EF. No RabbitMQ/Rebus transport or
business contract dependency. PostgreSQL is explicit: incompatible provider/model setup
fails when resolving the producer/dispatcher, before publication.

```csharp
// In the module's native OnModelCreating:
model.ConfigurePostgresOutbox("inventory", "outbox_messages");

// In both native save overrides, before base:
this.ValidateOutboxChanges();

// Native context registration remains consumer-owned.
services.AddPostgresOutbox<InventoryDbContext>();
services.AddPostgresOutboxDispatcher<InventoryDbContext, InventoryPublisher>(
    new OutboxDispatchOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
```

Producer and dispatcher registrations are independent and scoped. The publisher/options
are bound to the specified context, not a global IMessagePublisher. Each context owns one
outbox table; modules can use separate schemas/table names. Existing tenant ownership/filter
utilities can protect TenantKey explicitly; tenantless rows are also supported.

Producer journey:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
// Decide/change business state using native concurrency predicates.
outbox.Enqueue(message);
await database.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

Dispatch uses an independent context with no pending business changes, native/enlisted or
ambient transaction. It owns short ReadCommitted claim and completion/retry transactions.
FOR UPDATE SKIP LOCKED skips locked candidates; a fresh lease token, database-clock expiry
and attempt increment are committed before publication. Completion or retry requires the
same token, undispatched status and an unexpired lease. Stale publishers cannot complete or
defer a successor's claim. No database transaction/row lock is held during network work.

Publisher failure records lease-fenced failure observations and selects the consumer's delayed
retry or dead-letter decision, then rethrows. Without a policy, the configured retry delay
remains indefinite. OutboxFailureAccountingException preserves publication/accounting errors
and faults the optional worker. OutboxCompletionException carries MessageId and the original
InnerException when completion is unconfirmed after normal publication return, including
completion-stage cancellation. It also faults the worker without another poll; requested host
shutdown ends normally. An uncommitted completion leaves the claim recoverable after expiry;
a missing commit response may leave the row completed. Inspect fresh storage after repair.
Completion faults never invoke publisher-failure accounting. Publication cancellation/abandonment
can likewise leave retained claims for expiry; transport acceptance remains uncertain.
RetryDelay may be zero; LeaseDuration must be positive. No hidden producer or transport retry.

The schema has a stable GUID primary key, JSONB payload, optional tenant key, database-generated
queued/available timestamps, dispatched timestamp, lease token/expiry and long attempts.
Consumer migrations create/update it, including recovery counts, revision, safe failure metadata
and dead-letter/redrive times. Attempts counts committed claims. The candidate index excludes dispatched/dead-lettered records;
retention still belongs to a later capability. Retain the fixed column/key/JSONB/default mapping;
schema/table names and ownership/filter configuration are customizable.
JSONB preserves JSON meaning, not original text formatting, property order or numeric spelling.
JsonElement is used for an owned opaque payload; no JSON query abstraction is provided.

Dispatch deliberately executes privileged table-wide SQL across owners; query filters do
not authorize that operation. Restrict invocation and grant the host appropriate database
permissions. A publisher needing tenant business access establishes a different admitted
scope. This dispatcher does not impersonate each owner or infer trust from the payload.

At-least-once delivery requires continued dispatch, accepting transport and retained records.
Duplicate effects remain receiver-owned. There is no FIFO, global concurrency cap, lease
renewal, pruning or exactly-once promise. Consumer-selected stop policy and separately registered
AddPostgresOutboxDeadLetters provide bounded List/Get and identity-preserving Retry. See
[outbox recovery, supervision and schema/grant obligations](../docs/outbox-recovery.md), the
[supported and deferred contract](../docs/capabilities.md) and actual
[Inventory RabbitMQ publisher](../../../samples/Wholesale/EventPersistenceDemo/InventoryRabbitMqPublisher.cs).

`await database.ReadPostgresOutboxBacklogAsync(routeKey, cancellationToken)` reads aggregate
counts and oldest eligible age using one database statement time on a fresh independent context.
It registers no services and takes no claims/row locks. Native grants/RLS apply; EF filters do
not. A metadata-only SELECT role needs six lifecycle columns and schema USAGE, with no payload
SELECT or UPDATE. Existing dispatch grants suffice. See [snapshot semantics and native consumer
health/metrics](../docs/outbox-observability.md); no schema/grant migration is introduced.

Inbox-only consumers configure/register the inbox independently and need no outbox table or
publisher. Intake requires caller-owned native ReadCommitted isolation. A retained key combines
subscription, producer and message ID; incompatible envelope redelivery throws rather than
silently deduplicates. A separate comparison statement observes a concurrent winning insert.
Payload equality uses PostgreSQL JSONB values, including numeric equality, not a lexical hash.

Processing holds FOR UPDATE SKIP LOCKED through bounded local handler work, SaveChanges,
completion and native commit. No inbox lease expires while the handler owns that transaction.
On failure before commit, savepoint rollback undoes local effects while retaining the original
delivery lock. Native SQL commits only failure accounting and consumer-selected retry/dead-letter state.
No later transaction writes obsolete retry metadata after releasing the lock. Without a keyed
policy, positive RetryDelay remains the default. Separately registered AddPostgresInboxDeadLetters
provides typed metadata list/detail and re-enables a
dead-lettered row at an observed revision. Consumer authority, forward migrations and restricted
grants remain explicit; see [recovery setup](../docs/inbox-recovery.md). Cancellation/connection
loss may leave no recorded failure; a commit response may be ambiguous.
DeadLetteredAt retains the physical `quarantined_at` mapping. The dead-letter lookup index
is a forward metadata-index migration, with no data rewrite or grant change. Reads use typed
EF projections, respect query filters/RLS and return no payload; cursors are scoped positions.
Rollback/policy/accounting SQL or accounting-commit failure throws
InboxFailureAccountingException with delivery identity and both original errors. Accounting
is unconfirmed, not necessarily uncommitted. The optional inbox worker faults on this exception;
native host supervision and consumer repair/restart policy govern what follows.

This protocol does not make another module/context or external side effect atomic. Native
business concurrency predicates remain necessary for different deliveries touching shared state.
See [full inbox setup/errors/limits](../docs/inbox.md).

See [durable message observability](../docs/observability.md) for optional W3C context, schema upgrades, native
activity/meter names, failure logs and host-owned exporter configuration.
