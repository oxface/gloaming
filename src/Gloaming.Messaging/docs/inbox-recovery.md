# Inbox dead letters and operator retry

PostgreSQL inbox processing records confirmed failures durably, supports consumer-selected
dead-letter state, and provides separately registered retry of the unchanged retained delivery.
This extends [ordinary inbox processing](inbox.md). The comparable
[outbox store](outbox-recovery.md) retains its separate lease-fenced publication protocol.
A dead letter is the original retained row in a stopped state, not a second physical queue.
The public name replaces quarantine; DeadLetteredAt retains the physical `quarantined_at` column.

## Consumer classification and stop policy

```csharp
services.AddPostgresInboxProcessor<RenderDbContext>(
    new InboxProcessingOptions(TimeSpan.FromSeconds(1)));
services.AddInboxFailurePolicy<RenderDbContext, RenderInboxFailurePolicy>("rendering.jobs");
```

Implement `IInboxFailurePolicy<TDbContext>.Decide(InboxFailure)`. Return either
`new InboxFailureDecision.RetryAfter(positiveDelay, safeCode)` or
`new InboxFailureDecision.DeadLetter(safeCode)`. The [rendering policy](../../../samples/InboxDemo/RenderInboxFailurePolicy.cs)
classifies PostgreSQL serialization/deadlock failures and command conflicts. Its three-failure
budget, delay and codes are consumer choices, not library defaults.

Registration is scoped and keyed by context/subscription; duplicates reject. The policy receives
the original exception, subscription, retained envelope and proposed counts including this failure.
Decide synchronously without I/O or business mutation. Codes must be nonblank and at most 128
characters; use a safe vocabulary, never exception text, credentials or payloads. Invalid/null
decisions and policy exceptions abort accounting, preserving both errors in
InboxFailureAccountingException (derived from AggregateException).
These are trusted application-code obligations, not a sandbox against context misuse.

Omission retains indefinite retry by the existing positive InboxProcessingOptions.RetryDelay,
with technical code `processing-failed`. No default poison threshold is supplied. Admission,
decoding and the meaning of recoverable/permanent failure remain consumer-owned.

## What commits after a failure

The processor acquires its delivery row lock **before** establishing a native savepoint.
Handler/save/completion failure rolls local work back to that savepoint, including earlier
executed saves and outgoing SQL. The original delivery lock remains held. Parameterized native
SQL commits only the failure decision/metadata in the surviving transaction. Failed tracked
proposals are never saved again; dispose the entire scope.

FailureCount is lifetime committed failure observations. FailuresSinceRedrive counts them since
the latest applied retry. Both increment once with Revision, LastFailedAt and LastFailureCode.
Retry sets database-clock AvailableAt; dead-letter state sets DeadLetteredAt and excludes selection.
Success retains failure/redrive evidence and increments revision with completion. All recorded
times come from PostgreSQL.

Recorded failure still throws the original processing exception; dead-letter state is not Processed.
The optional worker logs it, applies its operational delay and starts a fresh scope without
resetting counts. Other processors skip the locked delivery and process unrelated work, including
while rollback/accounting is in progress.

Rollback, policy, metadata SQL or accounting commit failure throws
InboxFailureAccountingException. Its SubscriptionKey/ProducerKey/MessageId identify the retained
delivery; ProcessingFailure and AccountingFailure preserve the original errors, also retained
in that order by InnerExceptions. Accounting was not confirmed: a lost commit response may
still leave the metadata committed. Disposal rolls back uncommitted work. No later transaction
writes retry metadata after releasing the lock, so an obsolete attempt cannot overwrite a successor.

Cancellation, connection loss and process death do not guarantee a recorded failure. A transaction
that cannot recover its savepoint is disposed without another accounting write. Counts describe
committed observations, not all handler entries/crashes. A cap limits recorded failures across
replicas/restarts, not every execution. Noncooperative handlers can still hold locks/delay shutdown.
Once a commit is attempted, a missing response is ambiguous. Never infer rollback or issue a
compensating failure write; fresh storage determines completion/dead-letter state/eligibility.

## Supervision when accounting is unconfirmed

The optional AddInboxWorker stops polling, logs a distinct critical diagnostic, and faults
its BackgroundService execution task on InboxFailureAccountingException. It does not apply
FailureDelay or start another handler attempt for that fault. Repeated handler execution cannot
repair a persistently broken accounting path or reliably consume its durable failure budget.
Requested worker shutdown during accounting ends the worker without another poll; direct
processor callers still receive both errors in the typed exception.

Native .NET HostOptions.BackgroundServiceExceptionBehavior governs the host's response.
StopHost, the native default, begins shutdown of the whole host, including its other services.
Ignore leaves the host running with this worker faulted; it does not restart the worker.
Direct processor callers own equivalent supervision. Alerting, readiness, deployment restart
policy and process exit status remain consumer responsibilities. The library installs no
host-wide supervision setting.

Repair the accounting dependency, then restart the worker/host and inspect fresh storage.
An unrecorded failure leaves the last committed row state; a missing commit response can
instead leave committed retry/dead-letter state. The existing authorized operator retry applies only
to an actually dead-lettered row at its observed revision. It is not a command to restart a
faulted worker. Stopping contains repeated selection but pauses all messages on that worker
until it is restarted; it does not promise progress while accounting remains broken.

This supervision adds no lease, durable started-at marker or global execution budget.
Process death and other unrecorded attempts retain the limits above. See the
[supervision report](../../../docs/reports/m2-inbox-failure-supervision.md) for current proofs.

## Authorized retry of retained work

```csharp
services.AddPostgresInboxDeadLetters<RenderDbContext>();

// Authenticate/authorize first; select permitted subscription/producer in consumer code.
var page = await deadLetters.ListAsync(
    "rendering.jobs", "exports", new(PageSize: 25), cancellationToken);
var item = await deadLetters.GetAsync(
    "rendering.jobs", "exports", messageId, cancellationToken);
InboxRetryResult result = await deadLetters.RetryAsync(
    "rendering.jobs", "exports", messageId, observedRevision, cancellationToken);
```

IInboxDeadLetters requires a fresh context with no tracked entities or native/enlisted/ambient
transaction. It owns a short ReadCommitted transaction and needs no handler, worker or publisher.
Resolving it grants no authority; consumer authorization, database grants and RLS govern the row.

ListAsync returns a detached InboxDeadLetterPage with at most 100 items (default 50) and
an optional NextCursor. The required subscription/producer select one exact lane; an optional
FailureCode matches exactly. Blank/over-128-character codes and page sizes outside 1–100 reject.
Pass NextCursor as InboxDeadLetterQuery.After for the next page; page size may change.
Cursors bind the lane and filter, and malformed/unsupported/mismatched tokens reject before
database work. They are positions, not authorization credentials or a multi-page snapshot.
That binding covers the explicit subscription/producer/failure-code inputs; consumers preserve
the same authorization, query-filter and RLS context across pages. Hidden consumer filters
are not encoded in the token.

Ordering is database dead-letter time then database UUID order. In an unchanged result set,
tied timestamps page without skipping or repeating stable rows. Retried rows disappear, and
a delivery dead-lettered again can move forward and appear again. An empty continuation page
is possible after concurrent retries. There is no total count or retained change feed.

GetAsync returns metadata for a currently dead-lettered, unprocessed row, or null for missing,
invisible, pending, redriven or completed work. Results include identity, wire alias/version,
revision, safe failure code, counts and lifecycle times. They exclude payload, tenant,
correlation/causation, tracing and exception text. Inspection takes no processing claim or
row lock; its observed revision can become stale immediately.

List/Get use typed AsNoTracking EF projections in their own short ReadCommitted transactions.
They respect configured query filters and PostgreSQL grants/RLS. Native transaction-start
RLS setup runs through the consumer's existing EF interceptor. SELECT authority suffices for
inspection; retry needs its existing UPDATE grants. Retry uses native SQL, so EF query filters
are not its authorization boundary. Authorize every invocation and establish admitted tenant
context/appropriate credentials before use.

Retried means a dead-lettered, unprocessed row at the observed revision became eligible at database
now. It clears dead-letter state, resets only the cycle count, increments revision/redrive count and
records last-redrive time. Lifetime failures and last-failure diagnostics remain. NotApplied means
missing/invisible, not dead-lettered, completed or stale state. SQL faults/cancellation throw. The
complete envelope, delivery identity, schema, receipt and diagnostic fields remain unchanged.

The conditional UPDATE uses ordinary row locking. A qualifying locked row waits and rechecks
after the competing transaction. Same-revision requests apply at most once. Active work outside
the dead-letter state is inapplicable and may return NotApplied immediately; retry does not interrupt a processor.
Stale requests cannot reopen a later dead-letter state. After response loss, reuse the original revision;
obtaining a new revision requires a new consumer operator-policy decision.

A later processor repeats current admission/decoding/business rules. Permanent conflicts can
be dead-lettered again. Equivalent intake still deduplicates dead-lettered, redriven and completed rows;
incompatible envelope reuse conflicts. No retention/deletion or external exactly-once guarantee.
Operator identity, approval/audit records, logs and presentation remain consumer policy. No generic
administration framework or audit store is supplied; the store provides bounded metadata reads
and one conditional retry. It provides no payload editor, discard or mark-processed operation.

## Schema and rollout

Migrations add zero-default nonnegative bigint `revision`, `failure_count`, `failures_since_redrive`,
`redrive_count`; nullable `last_failed_at`, `quarantined_at`, `last_redriven_at` timestamps and nullable
varchar(128) `last_failure_code`. The candidate index excludes dead letters. Native constraints enforce
counts/consistent metadata. DeadLetteredAt maps to the retained `quarantined_at` column; existing
data and grants are unchanged by the CLR rename. Forward Rendering/Inventory/Sales lookup
migrations add a partial index on `(subscription_key, producer_key, quarantined_at, message_id)`
where `processed_at IS NULL AND quarantined_at IS NOT NULL`. Tracked-save validation rejects all recovery edits; native/bulk writes
remain privileged and outside that guard.

Historical pending/completed rows retain identity/content/status and start with zero *known*
failures. Apply consumer-owned migrations before deploying the current binary. Stop pre-M2 processors
before relying on dead-letter state: their SQL ignores it. Mixed processor versions and rollback to
binaries ignoring dead-letter state are unsupported. Checkpoint M2 storage remains compatible;
the unreleased CLR/registration rename requires updating callers and provides no old-name aliases.

Restricted processors need SELECT plus UPDATE of `processed_at`, `available_at`, `revision`,
`failure_count`, `failures_since_redrive`, `last_failed_at`, `last_failure_code`, `quarantined_at`.
Recovery also needs its retry fields including `redrive_count`, `last_redriven_at`. Column grants
cannot distinguish trusted processor SQL from arbitrary SQL issued by the credential holder.
Review application authority and RLS explicitly.

Forward Rendering/Inventory/Sales migrations demonstrate storage changes. Existing restricted
Sales roles need an explicit grant update; finite provisioning is not a role reconciler and startup
performs no DDL. [Sales setup](../../../samples/Wholesale/WorkflowDemo/sales-inbox-rls.md) documents
that consumer. [InboxDemo](../../../samples/InboxDemo/README.md) shows optional operator usage;
[the dead-letter report](../../../docs/reports/m2-inbox-dead-letters.md) records current evidence
separately from the original M2 recovery/supervision results. Cleanup, payload editing,
schema-evolution rollout, global execution caps and template
presets remain deferred.
