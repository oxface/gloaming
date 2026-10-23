# Durable inbox setup and contract

Worker registration and polling options are supplied by the explicitly selected
[hosting package](../Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md), using the
`Gloaming.Messaging.EntityFrameworkCore.Hosting` namespace. Callable persistence/delivery
operations remain independently usable without this package.

Retain incoming delivery before acknowledging its transport, then process it separately.
Inbox-only use needs neither an outbox table/publisher nor event sourcing or context libraries.
PostgreSQL through Npgsql EF is the supported runtime.

## Explicit composition

```csharp
// Owning context's OnModelCreating; choose schema/table explicitly.
model.ConfigurePostgresInbox("rendering", "incoming_work");

// Both SaveChanges(bool) and SaveChangesAsync(bool,CancellationToken), before base:
this.ValidateInboxChanges();

// Native context registration/provider/migrations remain consumer source.
services.AddPostgresInbox<RenderDbContext>(); // Intake only is independently usable.
services.AddInboxHandler<RenderDbContext, RenderExportHandler>("rendering.jobs");
services.AddPostgresInboxProcessor<RenderDbContext>(
    new InboxProcessingOptions(TimeSpan.FromSeconds(1)));

// Optional, after processor/handler and native logging.
services.AddInboxWorker<RenderDbContext>("rendering.jobs",
    new InboxWorkerOptions(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
```

No marker/base DbContext, context discovery, automatic migrations, transport wiring or global
handler registry. All services are typed by the owning context; handler bindings additionally
use native keyed DI by subscription. Duplicate handler registration is a configuration error.
Each context maps one provided inbox record/table. Its public getters permit native EF diagnostics;
tracked lifecycle inserts/edits/deletes reject through the required save guard. Privileged SQL
and bulk writes are not sandboxed by that guard.

Subscriptions are processing lanes within that table, not separate tables or necessarily separate
producers. Each context/subscription selects one handler, which can decode multiple configured
message contracts/producers. Transport adapters choose the subscription explicitly; Gloaming does
not route subscriptions from queue names or payload types.

## Intake before acceptance

The adapter validates trusted producer binding, named schema/payload and any required tenant
admission before retaining work. ProducerKey is an assigned namespace, not proof of authentication.
TenantKey does not grant access. Core construction validates technical shape and owns a payload
clone; the adapter/handler chooses exact decoding and business compatibility.

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(
    IsolationLevel.ReadCommitted, cancellationToken);
InboxReceiveResult result = await inbox.ReceiveAsync(
    "rendering.jobs", admittedMessage, cancellationToken);
await transaction.CommitAsync(cancellationToken);
// Only now acknowledge the broker, or return successful HTTP acceptance.
```

ReceiveAsync executes parameterized native SQL immediately, not a tracked Add requiring a
later SaveChanges. It neither saves business changes nor commits. If intake includes other
tracked local changes, the caller explicitly saves those before commit. Both Queued and
AlreadyReceived are provisional until that transaction commits. Native/enlisted/ambient shared
transaction protocols and other isolation levels are rejected in this slice.

The primary key is (SubscriptionKey, ProducerKey, MessageId). Subscription is a stable receiving
registration, selected from trusted configuration rather than the wire body. Multiple subscriptions
can independently retain a fan-out delivery; producer namespaces are independent. Renaming either
key changes deduplication/dispatch meaning and needs deliberate migration.

AlreadyReceived compares a retained row whether pending or processed. Equal MessageName,
SchemaVersion, JSONB Payload, TenantKey, CorrelationId and CausationId are required. JSONB
object-property order/whitespace/equal numeric spellings compare as equivalent; array order
and changed values do not. Changed tenant metadata under the same key is a conflict, not a new
delivery. InboxMessageConflictException carries the conflicting key without payload/tenant
details in its error text. Transport code must not treat that conflict as accepted duplication.

The insert handles a competing unique key, then a separate ReadCommitted statement observes
the committed winner. A rolled-back winner permits the contender to insert. If retention
removes the winner before comparison, intake attempts insertion again. Three insert/compare
observations without a visible winner throw DbUpdateConcurrencyException without acceptance;
review native SELECT/RLS visibility or retry the caller-owned transaction after contention.
There is no application-level JSON hash/canonicalization engine.

Deduplication lasts while the receipt remains. Explicit [processed-receipt cleanup](inbox-retention.md)
ends that evidence: an old delivery can queue and execute again. ReceiptId identifies one
retained receipt generation, separately from the wire delivery key. Conditional operator retry
requires both observed ReceiptId and revision so an old request cannot target a later generation.

## Separate local processing

Use a fresh scope/context per attempt. The optional worker does so automatically.

```csharp
await using var scope = services.CreateAsyncScope();
var processor = scope.ServiceProvider.GetRequiredService<IInboxProcessor<RenderDbContext>>();
InboxProcessingResult result = await processor.ProcessNextAsync(
    "rendering.jobs", cancellationToken);
```

The processor owns ReadCommitted begin/save/completion/commit. It requires no pre-existing
native/enlisted/ambient transaction and no tracked entities. FOR UPDATE SKIP LOCKED selects
one currently eligible row for that subscription and holds its lock through local handler work.
A concurrent processor skips it and may handle another eligible row. Selection is best effort
by available/received time and delivery key, not FIFO. NoWork is not a globally empty backlog.

Multiple worker instances/processes can compete on the same subscription: their native row locks
exclude the same retained delivery while permitting other deliveries to run concurrently. Each
supplied worker processes sequentially; it does not manage a parallel pool, leader election or
partition assignment. Scale limits include database connections held through each handler,
business-row contention and idle polling. No throughput benchmark or optimal worker count is
claimed. Keep transactions short; a lease is not needed merely to add competing local processors.

IInboxHandler<TDbContext>.HandleAsync receives the retained envelope. The consumer decodes,
admits/establishes any tenant context before business queries and stages bounded local effects
on its owning injected context. It may enqueue a reply through IOutbox<TDbContext> in the same
transaction. Normal return permits processor save and completion; a handled business refusal
may therefore count as Processed. Handler exceptions fail the attempt.

Handlers must retain the processor's transaction and must not commit, roll back, replace it or
perform external effects in it. Enqueue external work instead. The processor checks transaction
association before final save; this is a contract for trusted application code, not a sandbox
against manual native commits/SQL. Another context's writes are not made atomic by resolving it
in the same DI scope. Different deliveries touching the same business row still need consumer
constraints/version predicates; inbox exclusion is only per retained delivery.

Failures before commit roll back business effects, completion and outgoing work, including an
earlier save. A savepoint established after locking the delivery permits local rollback and
durable failure accounting while retaining that original lock. A keyed consumer policy selects
delayed retry or dead-letter state; omission retains indefinite positive RetryDelay. Accounting failure
preserves both errors and delivery identity in InboxFailureAccountingException, derived from
AggregateException. Cancellation or an unrecoverable transaction does
not force accounting. Dispose the failed scope; commit response loss remains ambiguous. See
[failure accounting and authorized retry](inbox-recovery.md) for counters, policy and migration obligations.

This local database protocol needs no expiring inbox lease, renewal or ClaimLost result.
Outbox transport publication retains its separate lease/fencing protocol because acceptance
happens outside the database transaction.

## Metadata and operational limits

MessageId is stable delivery identity. CorrelationId groups related work; CausationId identifies
its immediate cause. Neither deduplicates business work. A new reply commonly inherits correlation
and sets causation to the incoming MessageId. Consumers perform this explicit mapping; there is
no ambient Gloaming metadata runtime.

The sequential worker validates processor/model/handler binding at startup, invokes fresh scoped
attempts, logs ordinary processing failures and applies positive idle/failure delays.
InboxFailureAccountingException instead faults the worker without another poll. Native host
supervision chooses the host response (StopHost by default); Ignore leaves the worker faulted.
Repair/restart and health monitoring remain consumer-owned. It does not apply
migrations, bind tenants or subscribe/acknowledge transport. Cooperative cancellation reaches
the handler; non-cooperative work can still delay shutdown and retain a database lock. Keep
handlers bounded, local and cancellable.

Rows remain retained unless separately authorized [processed cleanup](inbox-retention.md) removes them.
Unprocessed and dead-lettered rows are preserved. Consumer-selected dead-letter state and
identity-preserving operator retry are supported; classification, admission, thresholds and
operator authority remain consumer-owned. Counters record confirmed failures rather than every
execution/crash. No automatic pruning, permanent duplicate registry, second dead-letter queue, batch/parallel worker, global
ordering, automatic upcasting or other-provider guarantee is supplied.

Executable references: [HTTP inbox-only receiver](../tests/Fixtures/InboxHost/README.md),
[separate-context RabbitMQ round trip](../tests/Fixtures/BrokerJourney/README.md),
[Inventory integration handler](../../../samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueInboxHandler.cs).
[New proof report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/inbox1-durable-intake-processing.md) distinguishes
fresh results from O1 and archived receipt-only evidence.

TraceParent/TraceState are optional diagnostic fields, excluded from duplicate comparison.
The first committed intake retains its context even when equivalent retries carry different
send spans. See [observability](observability.md) for processing links, metrics and collection.

For explicit receiver-local historical decoding while retaining original schema/payload
deduplication evidence, see [queued-message compatibility](schema-compatibility.md).
