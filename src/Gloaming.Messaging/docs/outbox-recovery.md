# Outbox dead letters and operator retry

PostgreSQL dispatch records publisher-exception observations durably and supports
consumer-selected dead letters. An independently registered store provides bounded metadata
inspection and revision-conditional retry of the same retained message. Publication still
runs outside the database transaction. Dead letters stop future claims; they cannot retract
acceptance or cancel an obsolete publisher still running after lease expiry.

## Consumer failure policy

```csharp
services.AddPostgresOutboxDispatcher<ExportDbContext, HttpCommandPublisher>(
    new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
services.AddOutboxFailurePolicy<ExportDbContext, ExportOutboxFailurePolicy>();
```

Implement `IOutboxFailurePolicy<TDbContext>.Decide(OutboxFailure)`. Return
`new OutboxFailureDecision.RetryAfter(nonnegativeDelay, safeCode)` or
`new OutboxFailureDecision.DeadLetter(safeCode)`. One scoped policy belongs to one owning
context/table; duplicate registrations reject. The input supplies the unchanged envelope,
original publisher exception and proposed lifetime/cycle counts including this observation.
The consumer interprets route, wire contract and transport exception types.

Decide synchronously without I/O or business mutation. Codes must be nonblank and at most
128 characters, using a safe vocabulary rather than exception text, payload or credentials.
Null/invalid decisions, policy exceptions and overflow fail accounting. These are trusted
consumer-code obligations, not a sandbox against database misuse.

Omission retains indefinite retry by the existing nonnegative OutboxDispatchOptions.RetryDelay,
with code `publication-failed` and durable observations. There is no library poison threshold.
[Exports' policy](../../../samples/OutboxDemo/ExportOutboxFailurePolicy.cs) illustrates consumer
HTTP classification and a three-observation cycle budget; it does not govern RabbitMQ.
Direct construction can append the named `failurePolicy:` argument; existing three/four-argument
calls remain valid.

## Counts, leases and uncertain outcomes

| Field | Meaning |
| --- | --- |
| Attempts | Committed claims, including abandoned claims and acceptance followed by completion failure. |
| FailureCount | Lifetime committed observations of publisher exceptions under a valid claim. |
| FailuresSinceRedrive | Those observations since the latest applied operator retry. |
| Revision | Advances with committed claim, completion, failure accounting and operator retry. |
| RedriveCount | Applied operator retries; does not count repeated stale requests. |

An observed publisher exception is not confirmed transport rejection. A receiver may accept
before its response is lost. Claims abandoned by death/cancellation, stale publishers and
unconfirmed accounting need not increment failures. A consumer budget caps recorded
observations, not all executions or accepted deliveries.

Dispatch commits an eligible row's fresh token, expiry, Attempts and Revision before
publication. It holds no database transaction/row lock across transport work. After a
publisher exception, a short ReadCommitted transaction locks the still-current unexpired
claim, reads counts and invokes policy. The final UPDATE rechecks identity, token,
undispatched/non-dead-lettered state and database-clock expiry. Lock acquisition does not
renew the lease. It increments failure counts/revision, records safe code/database time,
sets delayed availability or dead-letter time, and clears the lease atomically. Lost or
expired claims make no accounting change. The original publication exception still throws.

After normal publication return, completion locks the current row and rechecks the same
fence at its final write. Published requires observed completion commit; zero changed rows
returns ClaimLost. Completion SQL/commit failures throw OutboxCompletionException: they never invoke
failure policy or a compensating failure write. A lease can expire while waiting for a lock;
both completion and accounting recheck after that wait. Competing dispatchers skip locked
rows and can process unrelated work during accounting.

Publication cancellation retains the claim for expiry. Successful acceptance followed by uncommitted
completion failure can redeliver the same MessageId/envelope after expiry. A missing commit
response can instead leave completion or accounting committed. Inspect fresh storage; do
not infer rollback or issue an unconditional recovery write. An obsolete publisher can
still accept externally after successor dead-lettering/operator retry, but its database
completion/accounting cannot change the successor's state. Receiver deduplication remains
necessary. No external exactly-once, global execution cap, FIFO or bounded shutdown guarantee.

## Accounting and completion faults, supervision

Lock/read, policy/validation, metadata SQL or accounting-commit failure throws
OutboxFailureAccountingException, derived from AggregateException. MessageId identifies the
retained work; PublicationFailure and AccountingFailure preserve both original errors,
also in that order in InnerExceptions. Accounting is unconfirmed, not necessarily uncommitted.
The operation disposes its transaction/context without a second accounting attempt or
unconditional lease release.

After the publisher returns normally, failure starting, executing, committing or disposing
the completion transaction throws OutboxCompletionException. MessageId identifies the retained
work and InnerException preserves the original error. It means completion was not confirmed;
the transaction may already have committed. It never invokes publication policy, consumes the
failure budget, marks a dead letter or releases the lease with a compensating write.
ClaimLost remains an ordinary result for a lost/expired fence; it does not fault the worker.

AddOutboxWorker logs a distinct critical diagnostic and faults without another dispatch
poll on either exception. Native HostOptions.BackgroundServiceExceptionBehavior governs the
host: StopHost begins host shutdown; Ignore leaves this worker faulted and does not restart
it. The library installs no host-wide option. Direct dispatcher callers own equivalent
supervision. For example, the executable OutboxDemo host explicitly selects:

```csharp
services.AddOutboxWorker<ExportDbContext>(
    new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
```

Callable consumers can catch OutboxCompletionException, record its MessageId, suspend that
dispatch lane and inspect fresh storage after repair. Existing catches for native completion
SQL/IO errors must now inspect InnerException. Completion-stage requested cancellation also
throws this typed fault with the original OperationCanceledException inside; attempt telemetry
still reports cancelled. Publication-stage requested cancellation keeps its existing
OperationCanceledException contract. Requested shutdown during accounting or completion ends
the optional worker normally without another poll. Direct callers still receive the typed
fault. Ordinary claim/publication failures retain FailureDelay and fresh scopes, provided
publication-failure accounting was confirmed.

Repair the failing dependency and inspect fresh state before restarting dispatch. A retained abandoned claim
waits for expiry. A dead letter requires separately authorized retry; restarting a worker
does not retry stopped work. A completed row needs no action. An undispatched row can publish
again after expiry with the same identity/payload. The existing dead-letter Retry is inapplicable
to both pending and completed work. There is no new recovery command for completion faults.

Stopping one worker contains its polling loop. Other dispatchers or an external supervisor
that repeatedly restarts an unrepaired host can still repeat accepted deliveries after expiry,
without consuming the failure budget. Host exit code,
alerting, readiness, process restart policy and progress while the worker is stopped remain
consumer responsibilities.

## Authorized inspection and retry

```csharp
services.AddPostgresOutboxDeadLetters<ExportDbContext>();

// Authenticate/authorize first; select the permitted route in consumer code.
var page = await deadLetters.ListAsync(
    "exports.render", new(PageSize: 25), cancellationToken);
var item = await deadLetters.GetAsync(
    "exports.render", messageId, cancellationToken);
var result = await deadLetters.RetryAsync(
    "exports.render", messageId, observedRevision, cancellationToken);
```

IOutboxDeadLetters requires no producer, publisher or worker. Each scoped operation owns a
short ReadCommitted transaction on a fresh context without tracked entities or native,
ambient or enlisted transactions. Dispatch retains its existing weaker restriction of no
pending tracked changes. Resolving a store grants no application or database authority.

List/Get project detached metadata for undispatched dead letters in one exact route. Results
exclude payload, tenant, correlation/causation, tracing, exception text and lease tokens.
Get returns null for missing, invisible, wrong-route, pending, retried or completed work.
Inspection takes no processing claim or row lock; its revision may become stale immediately.
Typed AsNoTracking queries respect configured EF filters and native grants/RLS.

OutboxDeadLetterQuery supports PageSize 1–100 (default 50), an optional exact nonblank
FailureCode of at most 128 characters, and an opaque After continuation. Ordering uses
database dead-letter time then PostgreSQL UUID order. At most PageSize + 1 metadata rows
are fetched. NextCursor binds the outbox schema/table, exact route and code filter; malformed,
unsupported or mismatched tokens reject before database work. Page size may change.

Paging observes live state, without a total count or multi-page snapshot. Retried rows
disappear; re-dead-lettered messages may move forward and appear again. A continuation may
return an empty page after concurrent changes. Cursors are positions, not authorization;
the consumer maintains the same admitted filter/RLS/credential context across calls.
Inbox and outbox share only their private position codec. Their public stores, DTOs and
transaction protocols remain distinct; existing inbox cursor representation is preserved.

Retry conditionally updates the route/message at the supplied revision only while
undispatched, dead-lettered and without a current lease. It clears dead-letter state,
sets availability to database now, resets only FailuresSinceRedrive, increments revision/
RedriveCount and records LastRedrivenAt. Attempts, lifetime failures, last-failure evidence,
queued time and every envelope field remain unchanged. A subsequent dispatcher repeats
current publisher validation; operator retry cannot force acceptance or completion.

Retried means eligibility was observed committed. NotApplied covers missing/invisible,
wrong-route, active/pending, completed or stale work. Ordinary row locking makes qualifying
updates wait and recheck; same-revision requests apply once. A stale repetition cannot
reopen a later dead letter. After response loss, repeat the original revision; choosing a
new revision is a new consumer operator-policy decision. SQL faults/cancellation throw.
Native retry SQL does not use EF query filters as an authority boundary. Authorize every
call and establish appropriate credentials/RLS context before invocation.

## Schema, grants and deployment

ConfigurePostgresOutbox adds zero-default bigint revision/failure_count/failures_since_redrive/
redrive_count, nullable last_failed_at/dead_lettered_at/last_redriven_at timestamps, and
varchar(128) last_failure_code. Constraints enforce nonnegative counts, cycle <= lifetime <=
Attempts, consistent failure/redrive metadata, completion/dead-letter exclusion, and no lease
on either stopped or completed work. The candidate index excludes dead letters; a partial
`(destination, dead_lettered_at, message_id)` index supports inspection. Tracked producer
guards reject assigned recovery values and all later edits/deletes. Native/bulk writes are
privileged and remain outside those guards.

Consumer-owned forward Exports/Inventory/Sales migrations preserve legacy pending, leased
and completed rows. New fields start at zero/null; historical Attempts do not reconstruct
failures. Inbox retains its existing physical quarantined_at column; outbox uses the new
dead_lettered_at name. Apply migrations and reviewed restricted grants before deploying.
Stop old dispatchers before using dead letters: they ignore the exclusion predicate.
Mixed versions or rollback to binaries that ignore it are unsupported. Workers perform no DDL.

Dispatch needs SELECT plus UPDATE on lease_token, lease_until, attempts, available_at,
dispatched_at, revision, failure_count, failures_since_redrive, last_failed_at,
last_failure_code and dead_lettered_at. It does not need operator-only redrive_count or
last_redriven_at UPDATE. [Restricted Sales setup](../../../samples/Wholesale/WorkflowDemo/sales-outbox-dispatch.md)
verifies the actual effective grants, including PUBLIC/inherited privileges. Existing roles
need an explicit owner-reviewed grant update; finite provisioning does not reconcile roles.

Restricted EF producers also need SELECT on the four generated counter defaults returned
by INSERT: revision, failure_count, failures_since_redrive and redrive_count, alongside
queued_at/available_at. The restricted Sales reply producer provisions/verifies these six
columns explicitly and still has no payload SELECT or outbox lifecycle UPDATE. Existing
producer roles need a reviewed SELECT grant update too.

SELECT-only credentials can inspect. Retry additionally needs UPDATE on dead_lettered_at,
available_at, failures_since_redrive, revision, redrive_count and last_redriven_at, plus SELECT
on metadata and predicate columns including dispatched_at/lease_token/lease_until. A restricted operator
can use these column grants without payload SELECT. Column privileges do not enforce the
legal lifecycle protocol: a credential able to set dead_lettered_at can issue arbitrary SQL
to clear it. Database grants/RLS and trusted application authority remain separate obligations.

[OutboxDemo](../../../samples/OutboxDemo/README.md) provides executable fixed-route operator
endpoints and HTTP policy. [The slice report](../../../docs/reports/m2-outbox-dead-letters.md)
separates new PostgreSQL/HTTP/process evidence from earlier O1 and inbox M2 results.
The [completion supervision report](../../../docs/reports/m2-outbox-completion-supervision.md)
records the subsequent native-host, cancellation, backend-death and restart proofs; this
follow-up requires no schema, grant or registration changes.
Payload editing, completed-message replay, lease renewal, a generic administration
framework and a template preset are excluded.

Read-only [backlog snapshots and consumer worker health](outbox-observability.md) are separately
available without acquiring claims or retry authority. Counts are observations; their age and
native worker task state do not resolve transport/completion uncertainty.

[Completed-outbox retention](outbox-retention.md) is a separately authorized maintenance
operation. It never deletes pending/dead-letter work or gives dispatchers deletion authority.
Deleting sender history does not alter receiver deduplication or retract external acceptance.
