# Failed inbox work and operator retry

PostgreSQL inbox processing records confirmed failures durably, supports consumer-selected
quarantine, and provides separately registered retry of the unchanged retained delivery.
This extends [ordinary inbox processing](inbox.md); it does not change outbox recovery.

## Consumer classification and stop policy

```csharp
services.AddPostgresInboxProcessor<RenderDbContext>(
    new InboxProcessingOptions(TimeSpan.FromSeconds(1)));
services.AddInboxFailurePolicy<RenderDbContext, RenderInboxFailurePolicy>("rendering.jobs");
```

Implement `IInboxFailurePolicy<TDbContext>.Decide(InboxFailure)`. Return either
`new InboxFailureDecision.RetryAfter(positiveDelay, safeCode)` or
`new InboxFailureDecision.Quarantine(safeCode)`. The [rendering policy](../../../samples/InboxDemo/RenderInboxFailurePolicy.cs)
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
Retry sets database-clock AvailableAt; quarantine sets QuarantinedAt and excludes selection.
Success retains failure/redrive evidence and increments revision with completion. All recorded
times come from PostgreSQL.

Recorded failure still throws the original processing exception; quarantine is not Processed.
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
compensating failure write; fresh storage determines completion/quarantine/eligibility.

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
instead leave committed retry/quarantine. The existing authorized operator retry applies only
to an actually quarantined row at its observed revision. It is not a command to restart a
faulted worker. Stopping contains repeated selection but pauses all messages on that worker
until it is restarted; it does not promise progress while accounting remains broken.

This supervision adds no lease, durable started-at marker or global execution budget.
Process death and other unrecorded attempts retain the limits above. See the
[supervision report](../../../docs/reports/m2-inbox-failure-supervision.md) for current proofs.

## Authorized retry of retained work

```csharp
services.AddPostgresInboxRecovery<RenderDbContext>();

// Authenticate/authorize first; select permitted subscription/producer in consumer code.
// Inspect with AsNoTracking or another context, keeping this operation context fresh.
InboxRetryResult result = await recovery.RetryAsync(
    "rendering.jobs", "exports", messageId, observedRevision, cancellationToken);
```

IInboxRecovery requires a fresh context with no tracked entities or native/enlisted/ambient
transaction. It owns a short ReadCommitted transaction and needs no handler, worker or publisher.
Resolving it grants no authority; consumer authorization, database grants and RLS govern the row.

Retried means a quarantined, unprocessed row at the observed revision became eligible at database
now. It clears quarantine, resets only the cycle count, increments revision/redrive count and
records last-redrive time. Lifetime failures and last-failure diagnostics remain. NotApplied means
missing/invisible, nonquarantined, completed or stale state. SQL faults/cancellation throw. The
complete envelope, delivery identity, schema, receipt and diagnostic fields remain unchanged.

The conditional UPDATE uses ordinary row locking. A qualifying locked row waits and rechecks
after the competing transaction. Same-revision requests apply at most once. Active nonquarantined
work is inapplicable and may return NotApplied immediately; retry does not interrupt a processor.
Stale requests cannot reopen a later quarantine. After response loss, reuse the original revision;
obtaining a new revision requires a new consumer operator-policy decision.

A later processor repeats current admission/decoding/business rules. Permanent conflicts can
quarantine again. Equivalent intake still deduplicates quarantined, redriven and completed rows;
incompatible envelope reuse conflicts. No retention/deletion or external exactly-once guarantee.
Inspect with native Set<InboxMessageRecord>().AsNoTracking() using authorized consumer filters/DTOs.
Operator identity, approval/audit records, logs and presentation remain consumer policy. No generic
listing/administration framework or audit store is supplied.

## Schema and rollout

Migrations add zero-default nonnegative bigint `revision`, `failure_count`, `failures_since_redrive`,
`redrive_count`; nullable `last_failed_at`, `quarantined_at`, `last_redriven_at` timestamps and nullable
varchar(128) `last_failure_code`. The candidate index excludes quarantine. Native constraints enforce
counts/consistent metadata. Tracked-save validation rejects all recovery edits; native/bulk writes
remain privileged and outside that guard.

Historical pending/completed rows retain identity/content/status and start with zero *known*
failures. Apply consumer-owned migrations before deploying the current binary. Stop old processors
before relying on quarantine: their SQL ignores it. Mixed processor versions and rollback to
binaries ignoring quarantine are unsupported.

Restricted processors need SELECT plus UPDATE of `processed_at`, `available_at`, `revision`,
`failure_count`, `failures_since_redrive`, `last_failed_at`, `last_failure_code`, `quarantined_at`.
Recovery also needs its retry fields including `redrive_count`, `last_redriven_at`. Column grants
cannot distinguish trusted processor SQL from arbitrary SQL issued by the credential holder.
Review application authority and RLS explicitly.

Forward Rendering/Inventory/Sales migrations demonstrate storage changes. Existing restricted
Sales roles need an explicit grant update; finite provisioning is not a role reconciler and startup
performs no DDL. [Sales setup](../../../samples/Wholesale/WorkflowDemo/sales-inbox-rls.md) documents
that consumer. [InboxDemo](../../../samples/InboxDemo/README.md) shows optional operator usage;
[the M2 report](../../../docs/reports/m2-inbox-failure-recovery.md) records fresh evidence. Outbox
recovery, cleanup, payload editing, schema-evolution rollout, global execution caps and template
presets remain deferred.
