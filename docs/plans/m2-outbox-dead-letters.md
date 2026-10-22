# M2 outbox dead letters and operator retry

Status: interface and scope approved by the owner's “proceed with implementation”,
2026-10-21. Implemented for owner line-by-line review; the [current family contract](../../src/Gloaming.Messaging/docs/outbox-recovery.md)
and [slice report](../reports/m2-outbox-dead-letters.md) describe current behavior and fresh
evidence separately from the opening baseline below. This checkout is on
`feat/outbox-dead-letters`, created without upstream
tracking from fetched `origin/main` `f4f694018b03c0d43c1707086ca3673e0352a6d6`. The worktree
and index were clean before branching. Main already contains the inbox recovery and
dead-letter work, including squash checkpoint `57dfeb3`; no inbox branch integration is needed.

An authorized operator can discover stopped outgoing work, inspect bounded safe metadata,
repair a consumer precondition and make the same retained message eligible again. A consumer
failure policy chooses delayed retry or dead-letter state using durable publication-failure
observations. Publication remains outside the database transaction. The original envelope,
lease fencing and uncertain transport outcomes remain explicit.

## Existing behavior and comparison with inbox

The current implementation and consumer paths are the starting evidence:

- `PostgresOutboxDispatcher` commits a token, expiry and `Attempts` increment before calling
  `IMessagePublisher`. `FinishAsync` currently either completes or schedules the fixed retry
  delay under an unexpired matching token. Publication failure rethrows; a retry-write fault
  combines both errors in AggregateException. The outbox worker catches it and polls again.
- `Attempts` counts committed claims, including abandoned claims. It cannot supply a durable
  count of publication failures. Cancellation, death and a missing completion response do
  not establish a failed transport acceptance.
- `IInboxDeadLetters<TDbContext>` already combines List/Get/Retry. Its detached DTOs contain
  metadata, revision and lifetime/cycle failure counts; its query/page provide bounded live
  timestamp/UUID paging. `AddPostgresInboxDeadLetters` is independent of processing/hosting.
  Rendering fixes the subscription/producer lane and authorizes its native HTTP endpoints.
- `PostgresInboxProcessor` retains a row lock across local work, savepoint rollback and
  accounting. `InboxFailureAccountingException` faults its optional worker; the consumer's
  native HostOptions decides whether the host stops. Outbox publication cannot adopt that
  savepoint protocol because transport work runs after the claim transaction commits.
- `SalesDispatchDatabase` provisions and verifies a separate login with SELECT on the
  existing outbox and UPDATE on five delivery columns. It has no business-table access,
  producer INSERT, operator registration or tenant business context.

Use the inbox consumer pattern and terms: **dead letter**, `FailureCount`,
`FailuresSinceRedrive`, `Revision`, `DeadLetteredAt`, and revision-conditional operator retry.
Retain the separate typed stores. Inbox keys are subscription/producer/message;
outbox identity is message within its owning table, with a route constraint for consumer
operator lanes. Do not add an outbox recovery interface alongside its dead-letter store.

Keep the existing inbox public declarations and storage unchanged. New outbox DTOs expose
`RouteKey`, `QueuedAt` and `Attempts`; they do not invent inbox subscriptions, producer keys
or receipt times. Separate failure inputs/decisions preserve the different failure meanings
and delay rules: inbox requires positive delay; existing outbox permits zero. Separate retry
result names keep this addition compatible with current inbox callers.

Share the existing private timestamp/UUID cursor codec and position validation between the
two stores, preserving the current inbox token representation and scope calculation. The
outbox scope includes an outbox discriminator, mapped schema/table, route and failure-code
filter, so it cannot consume an inbox cursor. Keep lane selection, projections, lifecycle SQL,
lease handling and model validation specific to their records. Do not introduce a common
queue entity, public generic store, transaction framework or generic provider dialect.

## Proposed public surface

These declarations belong in `Gloaming.Messaging.EntityFrameworkCore`. They are the proposed
review surface, not compiled implementation.

```csharp
public sealed record OutboxFailure(
    OutgoingMessage Message,
    Exception Exception,
    long FailureCount,
    long FailuresSinceRedrive);

public interface IOutboxFailurePolicy<TDbContext>
    where TDbContext : DbContext
{
    OutboxFailureDecision Decide(OutboxFailure failure);
}

public abstract record OutboxFailureDecision
{
    private protected OutboxFailureDecision() { }

    public sealed record RetryAfter(TimeSpan Delay, string Code) : OutboxFailureDecision;
    public sealed record DeadLetter(string Code) : OutboxFailureDecision;
}

public interface IOutboxDeadLetters<TDbContext>
    where TDbContext : DbContext
{
    Task<OutboxDeadLetterPage> ListAsync(
        string routeKey,
        OutboxDeadLetterQuery query,
        CancellationToken cancellationToken = default);

    Task<OutboxDeadLetter?> GetAsync(
        string routeKey,
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<OutboxRetryResult> RetryAsync(
        string routeKey,
        Guid messageId,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}

public sealed record OutboxDeadLetterQuery(
    int PageSize = 50,
    string? FailureCode = null,
    string? After = null);

public sealed record OutboxDeadLetterPage(
    IReadOnlyList<OutboxDeadLetter> Items,
    string? NextCursor);

public sealed record OutboxDeadLetter(
    Guid MessageId,
    string RouteKey,
    string MessageName,
    int SchemaVersion,
    long Revision,
    long Attempts,
    long FailureCount,
    long FailuresSinceRedrive,
    DateTimeOffset QueuedAt,
    DateTimeOffset DeadLetteredAt,
    string LastFailureCode,
    DateTimeOffset LastFailedAt,
    long RedriveCount,
    DateTimeOffset? LastRedrivenAt);

public enum OutboxRetryResult
{
    Retried = 1,
    NotApplied = 2,
}

public sealed class OutboxFailureAccountingException : AggregateException
{
    public OutboxFailureAccountingException(
        Guid messageId,
        Exception publicationFailure,
        Exception accountingFailure);

    public Guid MessageId { get; }
    public Exception PublicationFailure { get; }
    public Exception AccountingFailure { get; }
}

// On a new OutboxServiceCollectionExtensions in the EF package:
public static IServiceCollection AddOutboxFailurePolicy<TDbContext, TPolicy>(
    this IServiceCollection services)
    where TDbContext : DbContext
    where TPolicy : class, IOutboxFailurePolicy<TDbContext>;

// On the existing PostgresOutboxServiceCollectionExtensions:
public static IServiceCollection AddPostgresOutboxDeadLetters<TDbContext>(
    this IServiceCollection services)
    where TDbContext : DbContext;
```

One scoped policy per context matches the current one-publisher/one-table dispatch binding.
Conflicting policy registrations reject. The policy can inspect the retained route/contract;
there is no route registry or policy-discovery framework. DI passes the optional policy into
the existing dispatcher. Preserve direct three/four-argument callers by appending the optional
parameter to its public constructor:

```csharp
public PostgresOutboxDispatcher(
    TDbContext database,
    IMessagePublisher publisher,
    OutboxDispatchOptions options,
    ILogger<PostgresOutboxDispatcher<TDbContext>>? logger = null,
    IOutboxFailurePolicy<TDbContext>? failurePolicy = null);
```

`Decide` runs synchronously without I/O or business mutation while accounting owns the row
lock. Its proposed counts include this observed exception. RetryAfter permits a nonnegative
database-clock delay. Both decisions require a safe nonblank code of at most 128 characters;
never persist exception text, payload or credentials. Null/invalid decisions, exceptions and
overflow are accounting faults. These are trusted consumer-code obligations.

Omitting the policy retains indefinite retry by `OutboxDispatchOptions.RetryDelay`, using
safe code `publication-failed`; it now records the observed failures. No poison threshold or
transport-exception classification is supplied by the library. A recorded failure still
throws the original publication exception. Existing dispatch outcomes remain unchanged:
NoWork, Published and ClaimLost do not acquire new meanings.

## Concrete consumer adoption

Extend the existing independent, tenantless `OutboxDemo`, rather than adding a new sample.
Its finite producer/dispatch journey remains available. Add explicit `--setup` and `--serve`
modes; `--serve --worker` opts into the existing outbox worker. Setup alone applies migrations.
An operator-only host needs the owning context and store, with no publisher or receiver URI.
Add a native ASP.NET Core framework reference to this console project for the optional host.

```csharp
// Dispatching host; the consumer registers its context and HTTP publisher separately.
services.AddPostgresOutboxDispatcher<ExportDbContext, HttpCommandPublisher>(
    new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
services.AddOutboxFailurePolicy<ExportDbContext, ExportOutboxFailurePolicy>();
services.AddOutboxWorker<ExportDbContext>(
    new(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

// Optional operator host; authenticate/authorize before invoking the store.
services.AddPostgresOutboxDeadLetters<ExportDbContext>();
var page = await deadLetters.ListAsync(
    "exports.render", new(PageSize: 25), cancellationToken);
var item = await deadLetters.GetAsync(
    "exports.render", messageId, cancellationToken);
var result = await deadLetters.RetryAsync(
    "exports.render", messageId, observedRevision, cancellationToken);
```

`ExportOutboxFailurePolicy` owns the HTTP example's rules. A route/contract mismatch stops
with `export-contract-unsupported`. Selected transient HTTP/connection failures retry with
`export-transport-transient` until the third recorded failure in a retry cycle, then stop
with `export-retry-limit`. Other exceptions stop with `export-review-required`. Treat HTTP
408/429/5xx and connection/time-out failures explicitly in that consumer code. Requested
dispatch cancellation bypasses classification. The HTTP acceptance condition stays a
successful response; even a publisher exception can follow remote acceptance.

The finite journey passes the same policy with the named constructor argument
`failurePolicy: new ExportOutboxFailurePolicy()`. RabbitMQ and Sales retain their current
default retry policy; they gain durable accounting through the library. HTTP classifications
are not imposed on a RabbitMQ publisher.

An optional `OUTBOX_DEMO_OPERATOR_TOKEN` configures sample bearer authentication and the
consumer permission `exports.outbox.retry`, following Rendering's existing native pattern.
Use constant-time comparison and reject short/empty configured tokens. Without configuration
the operator routes are absent. Production identity integration, credential lifecycle and
operator approval/audit remain consumer-owned. `OUTBOX_DEMO_URL` configures the listen URL.

- `GET /operations/outbox/dead-letters?pageSize=25&failureCode=...&after=...` returns a page.
- `GET /operations/outbox/dead-letters/{messageId:guid}` returns metadata or 404.
- `POST /operations/outbox/dead-letters/{messageId:guid}/retry` accepts only
  `{ "expectedRevision": 4 }`, returning 204 for Retried or 409 for NotApplied.

All routes require the consumer permission and fix `exports.render` in endpoint code. A URL,
payload or cursor cannot choose another route. Invalid inputs return 400. Authenticate and
authorize before storage invocation, including when the database is unavailable. Log operator,
message, expected revision and outcome. Retry does not publish inside the HTTP request; a
later dispatcher uses the current publisher against the unchanged envelope.

## Publication and durable accounting protocol

1. Keep the short native ReadCommitted claim transaction and FOR UPDATE SKIP LOCKED.
   Exclude dispatched and dead-lettered rows. Commit a new token, database-clock expiry,
   `Attempts + 1` and `Revision + 1` before invoking the publisher.
2. Publish without a database transaction or row lock. Claim/model/connection faults before
   publication do not invoke failure policy. Requested cancellation leaves the lease for
   expiry and does not increment failure counts.
3. If publication returns normally, use the existing separate conditional completion
   transaction. Require identity, matching token, unexpired lease, undispatched status and
   no dead-letter state; clear the lease and increment revision with completion. A zero-row
   completion returns ClaimLost. Completion SQL or commit failure propagates directly and
   **never** invokes publication policy or a compensating failure write.
4. If publication throws, open a short independent ReadCommitted accounting transaction.
   Lock the same message only when its token is still current, its lease is unexpired and
   it is undispatched/non-dead-lettered. Read durable failure counts under that lock, then
   invoke/validate the consumer decision. An obsolete/expired claim makes no accounting
   change and rethrows its original publication exception.
5. In the same transaction, conditionally update with the full token/state/expiry predicate
   again, using database time at this write. This second check covers expiry while waiting
   for a lock or running policy. Increment lifetime/cycle failures and revision; record
   failure time/code; set either delayed availability or dead-letter time; clear the lease.
   Zero changed rows means the claim expired and no proposed accounting applies. Commit
   only this metadata, then rethrow the original publication error.
6. A lock/read, policy/validation, metadata SQL, transaction or accounting-commit fault
   throws OutboxFailureAccountingException with both original errors, also in that order
   in InnerExceptions. Dispose the transaction/context. Do not retry accounting through a
   second transaction, release a lease unconditionally, or republish within this call.

The accounting row lock prevents concurrent claims while the final transition is decided.
Competing dispatchers skip the row and can dispatch unrelated work. Expiry remains a fence;
acquiring a lock does not renew a lease. Once the accounting transaction ends, an expired
retained claim can be taken by a successor. Neither an obsolete success nor an obsolete
failure may alter that successor, a newer dead letter, operator retry or completion.

Dead-letter state stops future automatic claims; it cannot stop an obsolete publisher that
is still running outside a transaction after its lease expired. That publisher can accept
externally even after a successor dead-letters the row or an operator retries it. Its
database completion/accounting remains fenced. A row with no lease does not prove there
are no external publications in flight.

`FailureCount` means **committed observations of publisher exceptions under a valid claim**.
It does not mean confirmed transport rejection. `FailuresSinceRedrive` is the corresponding
consumer-budget input. `Attempts` remains committed claims; abandonment and successful
acceptance followed by storage failure can increase Attempts without increasing failures.
Crash, cancellation, lost leases and accounting faults need not consume the failure budget.

A transport may accept and then lose its response, causing PublishAsync to throw. Policy
may retry or dead-letter that observation, but cannot retract acceptance. A successful
publication followed by completion failure remains recoverable by lease expiry with the same
MessageId and payload. A lost accounting/operator commit response can instead leave its
transition committed: inspect fresh storage, and repeat an operator request with its original
expected revision. No automatic failure classification is inferred from any missing response.

The optional OutboxWorker catches OutboxFailureAccountingException before its general catch,
logs a distinct critical message and faults without another dispatch poll. Native host
supervision and consumer repair/restart apply, matching the inbox operational pattern.
Requested shutdown during accounting ends the worker; direct callers still receive both
errors. Ordinary publication failures keep the worker's FailureDelay/fresh-scope behavior.
Completion faults retain their existing ambiguous behavior and can cause repeated accepted
deliveries after expiry; failure counts do not cap those deliveries. No global execution cap,
progress while the worker is stopped, bounded shutdown or external exactly-once claim follows.

## Inspection and identity-preserving retry

List/Get select only `dispatched_at IS NULL AND dead_lettered_at IS NOT NULL`, constrained
to the exact route. They use typed AsNoTracking projections in owned short ReadCommitted
transactions, respecting EF query filters and native grants/RLS. They take no processing
claim or row lock. Metadata excludes payload, tenant, correlation/causation, tracing, lease
tokens and exception text. Get returns null for missing, invisible, wrong-route, pending,
retried or completed work.

Match the inbox query contract: PageSize 1–100, default 50; optional exact safe failure code;
opaque versioned After cursor; database dead-letter time then PostgreSQL UUID ordering;
fetch at most PageSize + 1 metadata rows. Reject invalid/cross-scope cursors before database
work. Page size may change. This is live paging, with disappearing/re-dead-lettered rows,
possible empty continuation pages and no total count or multi-page snapshot. A cursor
grants no authority; keep the same admitted context/credentials across pages.

The store requires a fresh context with no tracked entities or native/enlisted/ambient
transaction. The existing dispatcher retains its current weaker requirement of no pending
tracked changes, so unchanged tracked reads in existing callers remain supported. Registration
is independent and scoped, with no producer, publisher, worker or application identity dependency.

Retry owns one native ReadCommitted transaction. Its conditional UPDATE includes route,
message, expected revision, undispatched/dead-lettered state and no lease. It clears only
dead-letter state, sets AvailableAt to database now, resets only FailuresSinceRedrive,
increments revision/redrive count and records last-redrive time. Preserve Attempts, lifetime
failures, last-failure evidence, original queued time and every envelope field. Dead letters
have no current lease; retry neither cancels an obsolete in-flight publisher nor forces
completion or creates a new envelope.

Retried means the eligibility transition was observed committed. NotApplied covers missing,
invisible, wrong-route, completed, active/pending or stale state. SQL faults/cancellation
throw. Same-revision operators apply once, including after waiting on a competing row lock.
A stale repeated request cannot reopen a later dead letter. Current publisher validation
runs again after retry, so a permanent incompatibility can dead-letter again. Consumer
authorization precedes invocation; native retry SQL does not treat EF filters as authority.

## Record, schema and restricted credentials

Add private-set getters to OutboxMessageRecord and explicit native mappings:

| CLR property | PostgreSQL column/type/default | Meaning |
| --- | --- | --- |
| Revision | `revision bigint NOT NULL DEFAULT 0` | Advances on claim, completion, recorded failure and applied operator retry. |
| FailureCount | `failure_count bigint NOT NULL DEFAULT 0` | Lifetime committed publisher-exception observations. |
| FailuresSinceRedrive | `failures_since_redrive bigint NOT NULL DEFAULT 0` | Such observations since the last applied retry. |
| LastFailedAt | `last_failed_at timestamptz NULL` | Latest recorded observation, database time. |
| LastFailureCode | `last_failure_code varchar(128) NULL` | Consumer-safe code. |
| DeadLetteredAt | `dead_lettered_at timestamptz NULL` | Excludes automatic claims. |
| RedriveCount | `redrive_count bigint NOT NULL DEFAULT 0` | Applied operator retries. |
| LastRedrivenAt | `last_redriven_at timestamptz NULL` | Latest applied retry, database time. |

Outbox uses a new `dead_lettered_at` column. Inbox retains its existing `quarantined_at`
physical mapping; terminology alignment does not justify rewriting inbox storage.

Keep the key, Attempts and full existing envelope/lease columns. Extend the current candidate
index filter to exclude dead letters. Add a partial lookup index on
`(destination, dead_lettered_at, message_id)` for undispatched dead letters. Add constraints
for nonnegative revision/counts, cycle failures <= lifetime failures <= Attempts, consistent
failure/redrive metadata, dispatched/dead-letter exclusion, and absence of leases on completed
or dead-lettered rows. Preserve the existing lease-pair constraint. Overflow fails visibly.
Extend producer added-row validation to require initial recovery values; reject tracked edits
as today. Native SQL/bulk writers remain privileged.

Forward `AddOutboxDeadLetters` migrations/designers and snapshots are required for
Exports (`exports.outgoing_work`), Inventory and Sales (`outbox_messages`). Rendering owns
no outbox migration. Existing pending, leased and completed records retain envelope, claim
count, times and completion. New fields start at zero/null; do not reconstruct failure history
from Attempts. Native EnsureCreated proof contexts acquire the new mapping directly.

Apply migrations and reviewed restricted-grant updates before deploying. Stop old dispatchers
before relying on dead-letter state: they ignore the new exclusion predicate. Mixed binaries
or rollback to a binary that ignores dead letters are unsupported. No worker performs DDL.

Extend Sales dispatch's finite grant inventory and effective-grant checks with `revision`,
`failure_count`, `failures_since_redrive`, `last_failed_at`, `last_failure_code`, and
`dead_lettered_at`, alongside its existing five delivery columns. Do not grant UPDATE on
`redrive_count`/`last_redriven_at`, producer INSERT, envelope changes or business tables.
Existing installations require an explicit owner-reviewed grant update; the finite setup
continues rejecting an existing role and does not become a reconciler. Startup performs no DDL.

Restricted proofs must use the actual login, including under the existing Sales command RLS
policies. Dispatch continues across tenants without a synthetic business-tenant scope.
Separate SELECT-only credentials prove inspection without mutation. An explicitly authorized
operator credential receives only the retry columns it needs in a proof; no production
operator provisioner or default Sales operator endpoint is added. Column grants constrain
columns, not legal lifecycle transitions: a credential that can set dead-letter state can
also issue arbitrary SQL changing it. Application authority, credentials and consumer RLS
remain necessary; registering a typed service grants none of them.

## Proposed file scope and review order

All paths below are repository-relative. No new package, solution project or runtime dependency
in a library is required.

| Area | Proposed new or edited files |
| --- | --- |
| Public contracts | `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/{OutboxFailure.cs,IOutboxFailurePolicy.cs,OutboxFailureDecision.cs,IOutboxDeadLetters.cs,OutboxDeadLetter.cs,OutboxDeadLetterQuery.cs,OutboxDeadLetterPage.cs,OutboxFailureAccountingException.cs,OutboxServiceCollectionExtensions.cs}`; append the optional dispatcher constructor parameter in the provider implementation. |
| Record, guards and supervision | Same EF directory: `OutboxMessageRecord.cs`, `OutboxModelExtensions.cs`, `OutboxWorker.cs`, `OutboxWorkerOptions.cs` documentation. Existing callable dispatch result/options stay compatible. |
| PostgreSQL mechanism | Provider directory: edit `PostgresOutboxDispatcher.cs`, model/registration extensions; add `PostgresOutboxStorage.cs`, `PostgresOutboxDeadLetters.cs`, `DeadLetterCursor.cs`; mechanically delegate inbox cursor handling to the shared codec in `PostgresInboxDeadLetters.cs`. |
| Library proofs | `src/Gloaming.Messaging/tests/OutboxPostgresTests/{FailureRecoveryTests.cs,DeadLetterTests.cs,FailureSupervisionTests.cs,OutboxConsumer.cs,DispatchTests.cs,RegistrationTests.cs,WorkerTests.cs,TelemetryTests.cs,EnqueueLifecycleTests.cs}`; rerun inbox paging/supervision proofs for shared-code compatibility. |
| Executable adoption | `src/Gloaming.Messaging/samples/OutboxDemo/{ExportOutboxFailurePolicy.cs,ExportOutboxOperations.cs,ExportOperatorAuthenticationHandler.cs,OutboxDemoHost.cs,Program.cs,DemoJourneys.cs,OutboxDemo.csproj,README.md}`; keep consumer policy out of libraries. |
| Adoption and process proofs | `src/Gloaming.Messaging/samples/OutboxDemo.Tests/{FailureRecoveryTests.cs,FailureSupervisionTests.cs,RecoveryMigrationTests.cs,AdoptionTests.cs,TraceMigrationTests.cs}`; `src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/OutboxRecoveryProcessTests.cs` launches the built optional OutboxDemo host through its existing ChildHost helper. Extend ChildHost's ambient-setting isolation for `OUTBOX_DEMO_` keys if needed. |
| Storage compatibility | Exports/Inventory/Sales `Migrations/*AddOutboxDeadLetters*` and model snapshots; no edit to historical migrations or inbox schemas. |
| Restricted adopter | `samples/Wholesale/WorkflowDemo/{SalesDispatchDatabase.cs,sales-outbox-dispatch.md}` and `samples/Wholesale/WorkflowDemo.Tests/{SalesDispatchTests.Recovery.cs,SalesDispatchTests.Startup.cs,SalesDispatchProof.cs}`. No default operator registration or transport-policy change. |
| Current family docs | Messaging family README, EF/Postgres READMEs, new `docs/outbox-recovery.md`, and `docs/{capabilities.md,inbox-recovery.md,observability.md}`. Link comparable operator patterns and state publication/accounting limits locally. |
| Repository records | This proposal and extraction-plan entry now; roadmap, design and focused `docs/reports/m2-outbox-dead-letters.md` after implementation. Record accepted irreversible choices in a focused ADR after owner review. Consider a general dead-letter glossary entry then; preserve the inbox definition until terminology is settled. |

Review public types and consumer calls first, then token/expiry checks and accounting-commit
boundaries, conditional operator SQL, schema/defaults/guards, grants, supervision and the
independent PostgreSQL observations. Approval of this scope would authorize implementation;
every library implementation still receives owner line-by-line review. Commit approval
remains separate and exact-change-set approval is required.

## Required new proofs after interface review

Use the existing real PostgreSQL 18.6 fixture. Observe database waits and independent durable
state with native connections and barriers; do not infer race ordering from sleeps. HTTP
adoption uses actual Kestrel acceptance. Child-process tests launch the built executable.

| Scenario | Required evidence |
| --- | --- |
| Durable budget | Fresh scopes/providers and a real killed/restarted dispatch process preserve observed failure counts/delay and consumer stop threshold. Restart after committed dead-letter state performs no automatic publication. Attempts remains distinct from failures. |
| Competing dispatchers | One valid claim per row; unrelated work progresses while publication/accounting is held. Expired takeover can publish the same identity; late success/failure cannot overwrite successor counts, lease, stop state or completion. Hold an obsolete publisher across successor dead-lettering and operator retry, then release it: external acceptance remains possible, but its database writes remain fenced. |
| Expiry during accounting | Hold the row at a verified lock barrier; expire a still-matching lease before the final write, both with and without successor takeover. No stale failure count, retry scheduling or dead-letter transition commits. |
| Competing operators | Two same-revision requests apply once. A qualifying UPDATE waits and rechecks after a competing transaction. Retry against active publication/pending/completed/wrong-route rows is inapplicable. An old request after re-dead-lettering cannot reopen it. |
| Repeated retry | Fail, stop, retry, fail/stop again, retry and succeed under the same message/envelope. Lifetime failures/Attempts accumulate; cycle failures reset only on applied retry; completion retains evidence and rejects retry. |
| Acceptance ambiguity | Actual HTTP receiver accepts, then completion SQL fails: no failure-policy invocation or failure-count increment; retained lease recovers after expiry and redelivers identical ID/payload. Also inject completion-commit response loss after a real commit and verify no compensating accounting/republication of completed work. |
| Publisher response loss | Actual receiver accepts but the publisher reports a transport failure. Record only the valid-claim exception observation; dead-letter state does not assert nonacceptance. Operator retry retains identity for receiver deduplication. |
| Accounting failure and supervision | Throwing/invalid policy, missing column grants, SQL constraints, backend termination and lost accounting-commit response preserve publication/accounting errors. No partial metadata or unconditional lease release. Worker logs the typed critical fault and makes no further poll under both native host supervision choices; repair/restart uses fresh storage. |
| Cancellation/death | Requested cancellation and process death during publication/accounting do not invent failures. A successor after lease expiry works; stale invocations cannot alter it. Shutdown during accounting preserves direct-caller errors without another worker poll. |
| Inspection | Exact route/code/lifecycle selection; detached metadata excludes sensitive fields; PageSize/cursor rejection before database work; tied-time PostgreSQL UUID ordering; live reads during uncommitted retry/accounting; disappeared continuation items and re-dead-lettered rows. Shared codec preserves existing inbox behavior/tokens. |
| Consumer authority | Absent routes without token, denied/missing credentials before database access, fixed route, malformed query/revision rejection, stale HTTP retries, current publisher validation after retry and metadata-only responses. |
| Restricted credentials | Actual Sales role records retry/dead-letter state across tenants without business access or operator-only updates. Missing accounting grants fault atomically and stop the worker; startup rejects incomplete/excessive grants. SELECT-only inspection works but retry fails; operator credentials are explicitly separate. |
| Upgrade and guards | Migrate legacy pending/leased/completed rows in all three owning schemas; preserve envelopes/Attempts/completion; zero known failure defaults; constraints and producer guards reject invalid lifecycle assignments; snapshots have no pending model changes. |

Run focused new cases, then the outbox/inbox, workflow-composition, independent HTTP adoption,
process and affected Sales/Inventory/consumer suites. Run the active build, relevant architecture
checks, CSharpier including generated migrations, analyzers/style, archive-integrity check and
`git diff --check` for the final scope. Existing CI already includes these projects; no new lane,
durable proof script or template preset is expected. If a durable script becomes necessary,
use the repository's TypeScript convention.

## Opening checkpoint evidence and remaining work

New baseline executions on unmodified fetched main, .NET SDK 10.0.401, 2026-10-21:

| Existing suite | Fresh result | What this establishes |
| --- | --- | --- |
| `src/Gloaming.Messaging/tests/OutboxPostgresTests` | 36 passed, 0 failed, 0 skipped | Existing producer/lease/dispatch/worker behavior, including stale claims and ambiguous completion. |
| `src/Gloaming.Messaging/tests/InboxPostgresTests` | 66 passed, 0 failed, 0 skipped | Current inbox accounting, inspection, retry and supervision comparison baseline. |
| `src/Gloaming.Messaging/samples/OutboxDemo.Tests` | 6 passed, 0 failed, 0 skipped | Existing independent EF/HTTP adoption, migrations and acceptance-before-completion-failure behavior. |

All used the repository's disposable PostgreSQL 18.6 containers through the local rootless
Podman socket, `DOTNET_PROCESSOR_COUNT=4`, and `dotnet test --project <project> --no-restore
--no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed`.
The initial sandboxed build exited without compiler diagnostics; rerunning with access to
the local runtime completed successfully. These are baseline reruns, not proofs of this
proposal. Earlier O1, I1 and inbox M2 reports remain dated historical evidence.

At the opening proposal checkpoint **no new reusable mechanism was proven**. The extraction candidate
is lease-fenced publication-failure accounting, metadata inspection and conditional retry;
the shared cursor codec would be a bounded internal consolidation. Consumer classification,
budgets, acceptance definitions, admission, operator authority, credentials and presentation
remain policy. Library implementation, executable adoption, schema/grant changes, the proof
matrix, family documentation and a final slice report follow owner interface/scope review.
No template change, payload editing, completed-message replay, retention or lease renewal
is included. The owner subsequently authorized implementation; the linked report records
its results. Changes remain unstaged; no commit or push is authorized by this proposal.
