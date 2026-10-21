# M2: failed inbox messages and operator redrive

The [dead-letter follow-up](m2-inbox-dead-letters.md) replaces the public quarantine/recovery
names below and adds bounded metadata inspection. This opening proposal retains review history.

Status: interface/scope approved by the owner's “proceed with implementation”, 2026-10-18.
The opening proposal below preserves the reviewed design. Implementation and proofs accompany
the [current family contract](../../src/Gloaming.Messaging/docs/inbox-recovery.md) and
[slice report](../reports/m2-inbox-failure-recovery.md); implementation remains uncommitted for
line-by-line owner review. The [extraction plan](library-extraction.md#m2-inbox-failure-recovery-interface-review)
indexes this capability.

## Outcome and scope

An inbox-only consumer can retain durable failure counts, stop automatic selection of a
failed delivery, and authorize an operator to make that same delivery eligible again.
Subscription, producer, message ID, named schema, JSONB payload, tenant, correlation,
causation, original receipt time and diagnostic context remain unchanged. Retained intake
continues to deduplicate equivalent deliveries in every lifecycle state, including quarantine.

Extend the existing Messaging EF/PostgreSQL packages; add no package or dependency.
`InboxDemo` supplies executable independent adoption without an outbox, tenancy, actor or
Access dependency. Classification, retry budget/delay, producer/tenant admission, operator
authentication/authorization and reason vocabulary remain consumer code. Quarantine means
the retained row is excluded from processing; it is not a second queue or transport rejection.

Outbox recovery, retention/deletion, payload editing, wire-schema evolution, generic management
queries/UI, automatic migrations, workflow changes and template presets are excluded.
Forward native migrations for the recovery columns are necessary storage changes, not a
queued-payload compatibility capability. Existing archived evidence stays untouched.

## Findings from the actual callers

- `PostgresInboxProcessor` owns one fresh-context ReadCommitted transaction, selects with
  `FOR UPDATE SKIP LOCKED`, calls the keyed handler, saves effects, completes and commits.
  `PostgresInboxStorage.DeferAsync` runs after that transaction is disposed. Its predicate
  checks only delivery identity and `processed_at IS NULL`.
- That predicate protects successor completion, as the current connection-termination test
  proves. It does not distinguish a newer pending failure or an operator retry. Adding
  counters/quarantine to that separate update would permit obsolete accounting to overwrite
  newer pending state. A revision read inside a fully rolled-back attempt would not itself
  identify all competing failed attempts: they could observe the same committed revision.
- `InboxWorker` supplies fresh scopes and an operational delay after exceptions. It owns no
  durable attempt budget. Worker restart must not reset a consumer's failure budget.
- `InboxDemoHost`, Inventory registration, Sales registration and the standalone workflow
  proof all register the existing processor. Rendering, Inventory, Sales and the independent
  workflow proof map the provided inbox record. Rendering, Inventory and Sales have native
  migration snapshots, so a shared record change requires forward migrations in all three.
- The restricted Sales reply consumer grants UPDATE only on `processed_at` and `available_at`.
  Its finite provisioning/actual-grant checks must recognize the new processor-written columns.
  Tenant-local context is established before selection in that mode. No operator recovery
  registration or authority is added to that worker.

## Proposed public interface

All new public types below live in `Gloaming.Messaging.EntityFrameworkCore`. They describe
one context's inbox; they introduce no transport or application-identity abstractions.
The code blocks show declarations for review, not compiled implementation.

```csharp
public sealed record InboxFailure(
    string SubscriptionKey,
    IncomingMessage Message,
    Exception Exception,
    long FailureCount,
    long FailuresSinceRedrive);

public abstract record InboxFailureDecision
{
    private protected InboxFailureDecision() { }

    public sealed record RetryAfter(TimeSpan Delay, string Code) : InboxFailureDecision;
    public sealed record Quarantine(string Code) : InboxFailureDecision;
}

public interface IInboxFailurePolicy<TDbContext>
    where TDbContext : DbContext
{
    InboxFailureDecision Decide(InboxFailure failure);
}

// Added beside AddInboxHandler on InboxServiceCollectionExtensions.
public static IServiceCollection AddInboxFailurePolicy<TDbContext, TPolicy>(
    this IServiceCollection services,
    string subscriptionKey)
    where TDbContext : DbContext
    where TPolicy : class, IInboxFailurePolicy<TDbContext>;

public interface IInboxRecovery<TDbContext>
    where TDbContext : DbContext
{
    Task<InboxRetryResult> RetryAsync(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}

public enum InboxRetryResult
{
    Retried = 1,
    NotApplied = 2,
}
```

The PostgreSQL registration extension adds:

```csharp
public static IServiceCollection AddPostgresInboxRecovery<TDbContext>(
    this IServiceCollection services)
    where TDbContext : DbContext;
```

Policy registration is scoped and keyed by subscription, just like handlers. Duplicate
policy registration rejects. Its synchronous decision has no database/network work or
business mutation; it receives the original exception and proposed counts including this
failure. Consumer policies can inspect the exception chain and admitted message metadata.
Positive finite retry delays and nonblank codes of at most 128 characters are validated
before native persistence. Exception text, stack traces and payloads are not failure codes.
The consumer may log details using its own logging policy.

An omitted policy retains existing indefinite retry using `InboxProcessingOptions.RetryDelay`
and a fixed technical code `processing-failed`; failure accounting still applies. This preserves
existing callers without silently selecting a poison threshold. The rendering consumer explicitly
registers its policy. A policy exception or invalid decision rolls back the attempt, including
proposed accounting, and exposes both processing and policy errors; no implicit classification
or quarantine choice replaces faulty consumer code.

`IInboxProcessor`, `IInboxHandler`, `InboxProcessingOptions` and `InboxProcessingResult` retain
their existing declarations. A recorded retry/quarantine still throws the original processing
exception after the metadata commit. `Processed` continues to mean committed local handling;
quarantine is never reported as successful processing. Existing telemetry reports a failed
processing attempt; no new telemetry instrument or management result hierarchy is needed.

The recovery operation is separately registered. It requires a fresh context, no tracked
entities and no native/enlisted/ambient transaction, and owns a short native ReadCommitted
transaction. Key arguments must be valid and expected revision nonnegative. It changes only a
quarantined, unprocessed row at the supplied revision. `NotApplied` covers missing/invisible,
completed, eligible, or stale state; it does not certify a queue is empty. SQL faults and
cancellation throw. Resolution/invocation grant no application authority: callers authorize
before using it, and database roles/RLS still govern the actual row.

Native `Set<InboxMessageRecord>().AsNoTracking()` supplies consumer-selected inspection.
No library list/search/payload-inspection interface is proposed.

## Concrete consumer adoption

```csharp
services.AddPostgresInbox<RenderDbContext>();
services.AddInboxHandler<RenderDbContext, RenderExportHandler>("rendering.jobs");
services.AddPostgresInboxProcessor<RenderDbContext>(
    new InboxProcessingOptions(TimeSpan.FromSeconds(1)));
services.AddInboxFailurePolicy<RenderDbContext, RenderInboxFailurePolicy>("rendering.jobs");
services.AddPostgresInboxRecovery<RenderDbContext>();

// Within RenderInboxFailurePolicy.Decide, after receiver-local exception classification:
return IsKnownTransient(failure.Exception) && failure.FailuresSinceRedrive < 3
    ? new InboxFailureDecision.RetryAfter(TimeSpan.FromSeconds(1), "render-transient")
    : new InboxFailureDecision.Quarantine("render-review-required");
```

Rendering's full policy distinguishes invalid/conflicting render commands from recognized
PostgreSQL serialization/deadlock failures and other storage faults. Invalid/conflicting or
unrecognized failures quarantine immediately; recognized recoverable failures get at most
three recorded failures per redrive cycle. These particular codes, classifications, limit
and delay are sample policy, not library defaults. Requested cancellation and infrastructure
failures that destroy the transaction are outside confirmed-failure accounting below.

The executable keeps its current public intake endpoint and adds an optional, explicitly
configured operator group using native ASP.NET Core authentication/authorization. A sample
bearer credential comes from `INBOX_DEMO_OPERATOR_TOKEN`, is compared in constant time, and
establishes a receiver-owned operator principal with `rendering.inbox.retry` permission.
No token is embedded in source or accepted as a query parameter. Without operator configuration
the routes are absent. Hosts reject empty/short configured credentials; deployment/credential
rotation and production identity-provider integration remain consumer responsibilities.

- `GET /operations/inbox/{messageId:guid}` requires that permission and inspects only the
  fixed `rendering.jobs`/`exports` lane. Its DTO contains identity, revision, lifecycle/counts,
  failure code/times and redrive metadata, excluding payload/exception text.
- `POST /operations/inbox/{messageId:guid}/retry` requires the same permission and accepts
  only `{ "expectedRevision": 4 }`. Subscription and producer come from receiver configuration.
  Authorization precedes recovery lookup/invocation. Success returns 204; stale/inapplicable
  state returns 409. Unauthenticated/unauthorized requests cannot mutate the row.

The operator reads current state, repairs a consumer precondition or deploys a compatible
handler when appropriate, then submits the observed revision. The callable library usage is:

```csharp
// Already authenticated/authorized by the consumer; a fresh operation scope.
InboxRetryResult result = await recovery.RetryAsync(
    "rendering.jobs", "exports", messageId, expectedRevision, cancellationToken);
```

Retry changes eligibility only. A processor subsequently re-runs current decoding/admission
and handler policy on the unchanged envelope. The operator cannot force acceptance, bypass a
handler, mark completion or reset total failures. Redriving a permanent conflict can quarantine
again. An old repeated HTTP request cannot re-open that later quarantine because its revision
is stale. Sample logs attribute the request/result to its operator; durable approval/audit
records are a separate consumer requirement, not supplied by the recovery row.

## Failure protocol and concurrency

Replace the post-transaction `DeferAsync` path, rather than expanding its pending-only update.

1. Begin the processor's existing ReadCommitted transaction. Select and lock one eligible
   unprocessed, nonquarantined delivery with `FOR UPDATE SKIP LOCKED`.
2. **After acquiring the delivery lock**, establish a named native savepoint. All handler work,
   earlier handler saves, the final save and completion SQL occur after it.
3. On success, complete the row, increment its revision and commit as today.
4. On a recoverable pre-commit exception, roll back to the savepoint. This undoes business SQL,
   completion and optional outgoing SQL, while retaining the inbox lock acquired before the
   savepoint. Do not invoke SaveChanges again on the rolled-back tracked proposals.
5. Invoke the consumer failure policy with incremented counts. Through parameterized native
   SQL in the surviving transaction, increment counts/revision and set last-failure code/time.
   Either set positive database-clock availability or set database-clock quarantine time.
   Commit only this metadata, then rethrow the original failure. Dispose the whole scope.
6. A failure while rolling back, deciding, recording or committing accounting exposes the
   original error plus the recovery error. Dispose/roll back; never release the lock and then
   issue an unfenced update through another transaction or re-save failed business proposals.

This use of savepoints follows PostgreSQL's documented lock lifetime and rollback behavior:
[explicit locking](https://www.postgresql.org/docs/18/explicit-locking.html#LOCKING-ROWS),
[ROLLBACK TO SAVEPOINT](https://www.postgresql.org/docs/18/sql-rollback-to.html), and native
[EF transaction/savepoint operations](https://learn.microsoft.com/en-us/ef/core/saving/transactions#savepoints).
The proposed application guarantee still requires new PostgreSQL tests, including an earlier
executed EF save and native constraint failures. Documentation alone is not proof.

Competing processors skip the locked delivery and can process unrelated rows during handling
and failure accounting. A qualifying operator UPDATE uses the same row's lock and revision;
after waiting it rechecks quarantine, completion and revision. An active nonquarantined row can
return NotApplied immediately rather than wait; retry does not interrupt it. Two operators with the
same revision produce one applied transition. Revision increments on failure, completion and
redrive, preventing repeated requests from reopening a later quarantine. Native locks supply
processing exclusion; no expiring inbox lease or public attempt token is added.

An operator retry atomically clears quarantine, sets availability to database now, resets only
`FailuresSinceRedrive`, increments revision/redrive count and records last-redrive time. It retains
total failures and last-failure diagnostics. Processing success also retains failure/redrive
evidence. Normal duplicate intake changes none of these lifecycle fields.

Requested cancellation rolls back/disposes without counting or quarantining. Connection loss,
process death or a transaction that cannot roll back to the savepoint causes no later accounting
write. The delivery remains recoverable from its last committed state. Failure counts mean
**committed observations of failed local work**, not every handler entry, crash, cancelled attempt
or infrastructure failure. This design does not promise a global cap on all started executions.
Stop policy applies to recorded failures and survives restart. This limit is part of owner review.

Once a completion or accounting commit is attempted, an exception may mean its response was
lost. Do not infer rollback or perform a compensating failure write. A fresh scope observes
retained completion/quarantine/counts; a repeated operator command uses the original expected
revision. Committed business effects cannot coexist with committed failure accounting for the
same attempt. No external exactly-once or bounded shutdown guarantee follows.

## Record and schema changes

Add public private-set getters to `InboxMessageRecord` and fixed native mappings:

| CLR property | PostgreSQL column/type/default | Meaning |
| --- | --- | --- |
| `Revision` | `revision bigint NOT NULL DEFAULT 0` | Monotonic lifecycle transition revision. |
| `FailureCount` | `failure_count bigint NOT NULL DEFAULT 0` | Lifetime confirmed failures. |
| `FailuresSinceRedrive` | `failures_since_redrive bigint NOT NULL DEFAULT 0` | Current consumer retry budget input. |
| `LastFailedAt` | `last_failed_at timestamptz NULL` | Database time of latest recorded failure. |
| `LastFailureCode` | `last_failure_code character varying(128) NULL` | Consumer-safe reason, never exception text. |
| `QuarantinedAt` | `quarantined_at timestamptz NULL` | Excludes the row from selection until authorized retry. |
| `RedriveCount` | `redrive_count bigint NOT NULL DEFAULT 0` | Applied operator retries. |
| `LastRedrivenAt` | `last_redriven_at timestamptz NULL` | Database time of latest applied retry. |

Retain the primary key/envelope columns. Extend the pending index filter to
`processed_at IS NULL AND quarantined_at IS NULL`. Add native constraints for nonnegative
counts/revision, cycle failures no greater than lifetime failures, completed/quarantined
exclusion, and consistent failure/redrive metadata. Overflow fails the transaction visibly.
Mapping validation includes new columns/defaults; the existing tracked-save guard already
rejects all lifecycle mutation, including the new fields. Native/bulk SQL remains privileged.

Forward `AddInboxFailureRecovery` migrations/designers and current snapshots are required in
Rendering, Inventory and Sales. Never edit old migration files. Old rows start with zero known
failures/revision and null recovery times; no historical failure count is invented. Exercise
upgrade from existing pending and processed rows, then verify native model snapshots.
Apply migrations before deploying the new processor binary; old processors must be stopped
before quarantine is used because they do not filter it. Mixed old/new processor operation and
rollback to binaries that ignore quarantine are unsupported. This is deployment coordination,
not an online schema-evolution feature.

The restricted Sales processor gains UPDATE on revision, failure counts/code/time and quarantine
columns only; it needs no redrive-column UPDATE. Its existing tenant policies remain. Finite
provisioning and effective-grant checks change together. Existing provisioned installations need
an explicitly reviewed grant update before deploying this binary; no grant reconciler or operator
login is added. Tests must prove missing grants fail and do not partially commit local work.

## Proposed file map and review order

Paths below are repository-relative. No new solution/project is needed.

| Area | New or edited files |
| --- | --- |
| Public types and composition | `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/{InboxFailure.cs,InboxFailureDecision.cs,IInboxFailurePolicy.cs,IInboxRecovery.cs,InboxMessageRecord.cs,InboxModelExtensions.cs,InboxServiceCollectionExtensions.cs}` |
| PostgreSQL mechanism | `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/{PostgresInboxRecovery.cs,PostgresInboxProcessor.cs,PostgresInboxStorage.cs,PostgresInboxModelExtensions.cs,PostgresInboxServiceCollectionExtensions.cs}` |
| Library proofs | `src/Gloaming.Messaging/tests/InboxPostgresTests/{FailureRecoveryTests.cs,InboxConsumer.cs,ProcessingTests.cs,RegistrationTests.cs,TelemetryTests.cs}`; existing intake/hosting coverage rerun. |
| Executable adoption | `samples/InboxDemo/{RenderInboxFailurePolicy.cs,RenderOperatorAuthenticationHandler.cs,RenderInboxOperations.cs,InboxDemoHost.cs,Program.cs,README.md}` |
| Consumer proofs | `samples/InboxDemo.Tests/{FailureRecoveryTests.cs,RecoveryMigrationTests.cs,TraceMigrationTests.cs}`; `samples/MessagingWorkerDemo.Tests/{WorkerProcessTests.cs,WorkerTopology.cs}` for new process restart/death evidence. |
| Required compatibility | Rendering/Inventory/Sales `Migrations/*AddInboxFailureRecovery*` and snapshots; `samples/Wholesale/WorkflowDemo/{SalesReplyDatabase.cs,sales-inbox-rls.md}`; restricted `SalesReplyTests.cs` assertions. |
| Family documentation | Messaging family README, EF/Postgres READMEs, `docs/{inbox.md,capabilities.md,observability.md}`; link supported recovery and retain excluded work as deferred. |
| Repository records | This plan, extraction-plan link/status, bounded roadmap status, `docs/reports/m2-inbox-failure-recovery.md`; after owner acceptance, a focused ADR and design link supersede ADR 0010's separate retry-write description. |

Review the types and consumer calls above first. Implementation review then focuses on
savepoint placement, commit-error paths, no post-rollback stale writes, conditional operator SQL,
auth-before-invocation, schema/defaults/grants and the independent assertions in PostgreSQL proofs.
No library implementation is approved by approving a future commit or a green baseline alone.

## Required new proofs and checks after approval

Use real PostgreSQL 18.6 through the existing fixture. Fault triggers/barriers must observe
actual database waits, not infer ordering from sleeps. Tests call the same public interfaces
as consumers and use native independent observations of effects and lifecycle state.

| Scenario | Required evidence |
| --- | --- |
| Durable retry budget | Multiple fresh processors record one failure per observed failed local operation; count/delay survive provider and real worker process restart; selected consumer threshold quarantines. |
| Whole local rollback | Handler exception after an executed save, native save failure and completion failure leave no business/reply effects but commit counts and the selected retry/quarantine state. |
| Accounting failure | Rollback-to-savepoint failure, metadata SQL/grant failure and metadata commit failure preserve original/recovery exceptions, never re-save business state, and leave no partial accounting. |
| Competing processors | Exactly one owns a delivery; another processes an unrelated message during the handler and while metadata SQL is held at a database barrier. Quarantined oldest work cannot starve unrelated rows. |
| Operator races | Two same-revision requests apply once; stale request after re-quarantine cannot re-open it. Retry racing a processor waits/rechecks, and cannot mutate a completed or newer state. |
| Rollback-to-metadata interval | Pause failure UPDATE after rollback; observe the original lock still held. A competing processor skips; operator retry cannot change the pending row. Separately observe qualifying operator UPDATEs wait and recheck revision. No obsolete deferred writer can overwrite successor state. |
| Connection/process death | Terminate the actual processing backend or child process before accounting; no effects/count invented. A fresh successor can process/fail and the old invocation cannot alter its eligibility, counts or quarantine. Restart after committed quarantine preserves stop state. |
| Repeated redrive | Same key/payload survives quarantine, authorized retry, failure, another retry and success; total count accumulates, cycle count resets only on applied retry, processed work rejects retry, stale HTTP repetitions do nothing. |
| Deduplication | Equivalent intake while quarantined/after redrive/after completion remains AlreadyReceived; changed payload/schema/tenant/correlation/cause still conflicts. Diagnostic context remains first-intake context. |
| Consumer authority | Missing/wrong credentials cause no mutation; authorized operator is constrained to fixed producer/subscription; current admission still runs after retry; permanent conflict can quarantine again. |
| Restricted Sales | Actual restricted role records a failure and continues under the existing admitted tenant; foreign-tenant rows remain untouched. Missing recovery-column grants fail atomically and startup checks reject incompatible grants. |
| Upgrade and telemetry | Legacy pending/completed rows preserve identity/content/completion; snapshots match; exceptions remain failed spans/logs, cancellation distinct, no payload/error text in durable codes. |

Run the focused new inbox/adoption/process/restricted-role cases, then the existing inbox,
workflow-composition and affected consumer suites. Verify relevant architecture checks, active
build, CSharpier/generated migration formatting, style/analyzers, archive integrity and
`git diff --check` according to the changed paths. No new CI lane or durable repository script
is expected: these projects already belong to the Messaging family lane.

## Historical evidence at the opening review point

Fresh baseline on unmodified `origin/main` `54ebe8d`, .NET SDK 10.0.401:

- Existing `InboxPostgresTests`: **34 passed, 0 failed, 0 skipped**, real disposable PostgreSQL,
  using the repository's rootless Podman/Testcontainers environment. This reruns current
  intake/processing/concurrency/rollback coverage; it does not prove M2 recovery.
- Existing `InboxDemo.Tests`: **13 passed, 0 failed, 0 skipped**, using disposable PostgreSQL,
  native RabbitMQ and Kestrel. This reruns existing adoption, migration and broker boundaries;
  it does not prove operator authentication, quarantine or redrive.

Both commands used `DOCKER_HOST=unix:///run/user/1000/podman/podman.sock`,
`TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock` and
`DOTNET_PROCESSOR_COUNT=4`, with `dotnet test --project <project> --no-restore --no-progress
--no-ansi --output Normal --show-stdout Failed --show-stderr Failed`.

The worktree/index were clean before branching from fetched main without upstream tracking.
At the proposal handoff only this proposal and its extraction-plan entry were changed, both
unstaged. `git diff --check` passed; `git diff --cached --exit-code` confirmed the staged
snapshot remained empty. No commit or push was made.

At the opening proposal checkpoint, no new reusable mechanism had been proven. The candidate was serialized failure
accounting plus a revision-conditional, identity-preserving retry operation. Consumer classification,
thresholds, admission, authentication/authorization and operator presentation remain policy.
There was no template change. The owner subsequently approved implementation; the linked current
contract and report distinguish new recovery evidence from these historical baseline results.
