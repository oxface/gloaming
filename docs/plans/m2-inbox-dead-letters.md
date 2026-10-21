# M2 follow-up: inbox dead-letter inspection and retry

Status: public interface and scope approved by the owner's “proceed”, 2026-10-21, before
merging the inbox branch. The reviewed proposal below is implemented for line-by-line review.
The starting
checkpoint is `f7d38d4` on `feat/inbox-failure-recovery`, with a clean worktree and empty index.
The [original recovery](m2-inbox-failure-recovery.md) and
[accounting supervision](m2-inbox-failure-supervision.md) remain the implemented mechanisms.
The [slice report](../reports/m2-inbox-dead-letters.md) records fresh evidence separately from
the earlier M2 results.

## Bounded consumer outcome

An authorized operator can discover a bounded page of dead letters, inspect safe metadata
for one retained delivery, repair its consumer precondition and request the existing
revision-checked retry. Rendering provides executable list/detail/retry endpoints, using
one context-typed dead-letter store. Authentication, permitted lanes, classification,
admission, safe reason vocabulary and operator policy remain consumer-owned.

Use **dead letter** for the lifecycle state currently called quarantine: the original inbox
row is retained and excluded from automatic selection until authorized retry. There is no
second physical queue, transport acknowledgement change, payload copy or new delivery identity.
Outbox could use the same vocabulary later, with a separately reviewed publication protocol.

This follow-up adds no console, payload access/editing interface, delete/discard/mark-processed
operation, retention cleanup, lease or crash budget, outbox recovery or generic management
framework. Resolution in this scope means authorized retry after a consumer-side repair.
The separate concern about numeric reader ordinals in the processing SQL remains deferred;
the new reads use typed EF projections and add no positional reader mapping.

## Existing callers and the selected seam

`IInboxRecovery<TDbContext>` already owns the conditional retry; PostgresInboxRecovery executes
its short native transaction. Rendering's operator endpoint independently repeats its own
AsNoTracking metadata selection and cannot list stopped work. Broker/process fixtures also
call recovery directly. The restricted Sales processor intentionally registers no recovery.

Replace that recovery interface with `IInboxDeadLetters<TDbContext>`, rather than adding a
second retry interface or forwarding store. List/Get concentrate lifecycle selection, bounded
paging, cursor validation and safe metadata projection; Retry retains the existing transaction
and revision predicate. The optional registration still requires neither a handler, worker,
publisher, outbox nor application identity library. Resolving it grants no authority.

## Proposed public interface

All declarations below live in `Gloaming.Messaging.EntityFrameworkCore`; they describe the
review surface, not compiled implementation.

```csharp
public interface IInboxDeadLetters<TDbContext>
    where TDbContext : DbContext
{
    Task<InboxDeadLetterPage> ListAsync(
        string subscriptionKey,
        string producerKey,
        InboxDeadLetterQuery query,
        CancellationToken cancellationToken = default);

    Task<InboxDeadLetter?> GetAsync(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<InboxRetryResult> RetryAsync(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}

public sealed record InboxDeadLetterQuery(
    int PageSize = 50,
    string? FailureCode = null,
    string? After = null);

public sealed record InboxDeadLetterPage(
    IReadOnlyList<InboxDeadLetter> Items,
    string? NextCursor);

public sealed record InboxDeadLetter(
    string SubscriptionKey,
    string ProducerKey,
    Guid MessageId,
    string MessageName,
    int SchemaVersion,
    long Revision,
    long FailureCount,
    long FailuresSinceRedrive,
    DateTimeOffset ReceivedAt,
    DateTimeOffset DeadLetteredAt,
    string LastFailureCode,
    DateTimeOffset LastFailedAt,
    long RedriveCount,
    DateTimeOffset? LastRedrivenAt);

// On PostgresInboxServiceCollectionExtensions in the existing PostgreSQL package:
public static IServiceCollection AddPostgresInboxDeadLetters<TDbContext>(
    this IServiceCollection services)
    where TDbContext : DbContext;
```

InboxRetryResult keeps its existing numeric values and behavior. The unreleased recovery
interface/registration are replaced in all active callers; no compatibility aliases or
duplicate retry operation are proposed.

Public terminology changes alongside the store:

| Current | Proposed |
| --- | --- |
| InboxFailureDecision.Quarantine(code) | InboxFailureDecision.DeadLetter(code) |
| InboxMessageRecord.QuarantinedAt | InboxMessageRecord.DeadLetteredAt |
| IInboxRecovery<TDbContext> | IInboxDeadLetters<TDbContext> |
| AddPostgresInboxRecovery<TDbContext>() | AddPostgresInboxDeadLetters<TDbContext>() |

The CLR rename retains the physical `quarantined_at` column explicitly. Existing checkpoint
databases, grants, constraints, historical migrations and retained rows need no column rename
or data rewrite. Current library/consumer language uses dead letters; schema documentation
identifies that legacy column mapping. Old generated migration designers remain historical.
New model snapshots and forward index migrations use the new CLR property.

## Read and pagination semantics

- List and Get select only `processed_at IS NULL AND quarantined_at IS NOT NULL` in the exact
  subscription/producer lane supplied by the authorized consumer. Get returns null for missing,
  invisible, pending, redriven or completed work. No result carries payload, tenant, tracing,
  correlation/causation data or exception text. Codes are the existing consumer-safe reasons.
- List accepts PageSize 1 through 100, default 50, and an optional exact, case-sensitive
  FailureCode. Null means no code filter; blank or over-128-character codes reject. Keys must
  be nonblank and are preserved exactly, matching existing identity semantics.
- Order by database dead-letter time ascending, then database UUID order for MessageId.
  Use forward keyset paging and fetch at most PageSize + 1 metadata rows. NextCursor, when
  present, points after the last returned item; no total-count query is included.
- After is an opaque, versioned position token containing the database timestamp/UUID and a
  fingerprint of the exact lane and code filter. Reject malformed, unsupported or mismatched
  cursors before database work. A cursor is neither authorization nor a secret; forging a
  position cannot widen the independently supplied lane. Page size may change between pages.
- Each call observes current committed state. Pagination is not a multi-call snapshot or a
  change feed. Retried rows disappear; a delivery dead-lettered again can move later and appear
  again. Stable rows with tied timestamps are paged exactly once in an unchanged result set.
  An empty page may follow a cursor when matching rows disappeared concurrently.
- List/Get use typed AsNoTracking EF projection in a short owned ReadCommitted transaction.
  The same provider/model and fresh-context guard as Retry reject tracked proposals and
  native/enlisted/ambient transactions. No caller business save occurs; results are detached.
  Consumer transaction-start RLS setup can run through its existing native EF interceptor.
- Reads respect configured EF query filters and PostgreSQL grants/RLS. Retry retains its
  native SQL contract; query filters are not invocation authorization for that operation.
  Consumers authorize every operation and establish required tenant context/credentials.
- Inspection takes no processing claim or row lock. It may observe a state whose revision
  becomes stale immediately afterward. Retry rechecks revision, unprocessed status and the
  dead-letter state under native row locking. Retried/NotApplied, commit ambiguity, identity,
  payload, deduplication and accounting-fault supervision retain their existing guarantees.

The installed Npgsql 10.0.3 package exposes native EF row-value comparison for server-side
timestamp/UUID keyset predicates. Implementation must prove database ordering and translation
against real PostgreSQL rather than compare GUIDs in application memory.

## Executable Rendering adoption

```csharp
services.AddPostgresInboxDeadLetters<RenderDbContext>();

// After consumer authentication/authorization; the permitted lane comes from configuration.
var page = await deadLetters.ListAsync(
    "rendering.jobs", "exports", new(PageSize: 25), cancellationToken);
var item = await deadLetters.GetAsync(
    "rendering.jobs", "exports", messageId, cancellationToken);
var result = await deadLetters.RetryAsync(
    "rendering.jobs", "exports", messageId, observedRevision, cancellationToken);
```

The optional configured operator host supplies:

- `GET /operations/inbox/dead-letters?pageSize=25&failureCode=...&after=...` returns the page.
- `GET /operations/inbox/dead-letters/{messageId:guid}` returns metadata or 404.
- `POST /operations/inbox/dead-letters/{messageId:guid}/retry` accepts only expectedRevision,
  returning 204 or 409 as today.

These replace the sample's unmerged recovery routes. All require its existing explicit
operator credential/permission; no configuration means no routes. The consumer fixes
subscription/producer, so URL fields/cursors cannot select another lane. Invalid query/cursor
input returns 400. Authentication/authorization precedes storage work. Retry request/result
logging keeps operator attribution. No UI, bulk action or durable operator audit is introduced.

## File/schema changes and required proofs

The approved implementation includes:

- Replace IInboxRecovery.cs with IInboxDeadLetters.cs and add the query/page/metadata declarations
  in the EF package. Rename the private PostgreSQL implementation, retain Retry's existing SQL,
  and add typed List/Get queries plus a private cursor codec. Update optional DI registration.
- Rename the decision/member throughout active library code, consumer policies, proofs, current
  family/package/sample docs and settled glossary/ADR language. Dated proposals/reports retain
  historical terminology with a link to the new contract; archive source remains untouched.
- Add a partial dead-letter lookup index on `(subscription_key, producer_key, quarantined_at,
  message_id)` for unprocessed dead letters. Rendering/Inventory/Sales get forward native
  index migrations and updated snapshots; existing columns/data/grants remain unchanged.
  This is an inbox metadata index change, not queued-payload schema evolution.
- Adopt the store in Rendering's operator endpoints and existing broker/process recovery
  fixtures. Restricted Sales still opts into no operator store by default.
- Real PostgreSQL proofs for lifecycle/lane/code selection, detached metadata and payload
  exclusion, limits/cursor rejection, tied-time UUID paging and last-item continuation.
  Verify filtered pages, empty results and cursor lane/filter mismatch.
- Real PostgreSQL read/write competition: inspection while accounting/retry is uncommitted,
  stale inspected revision, disappearing rows between pages, repeated dead-letter/redrive and
  restart, plus existing same-revision operator races, rollback, deduplication and unrelated
  processing proofs. Reads never reserve or prevent processing.
- Restricted-credential/RLS proofs for list, detail and retry against foreign-tenant rows,
  and SELECT-only inspection without UPDATE authority. Registration does not grant database
  privileges or authorize a mutation.
- HTTP proofs for authorized paged discovery/detail/retry, malformed queries, stale revisions,
  fixed lane, metadata-only responses, absent routes without configuration and denied requests
  before an unavailable database is touched.
- Native migration proofs from checkpoint M2 state, preserving a retained dead letter's full
  envelope, revision/counts/timestamps and operator retry. All three model snapshots must
  report no pending changes. Relevant family/sample/workflow/architecture checks accompany
  an updated family contract and separate slice report distinguishing mechanism from policy.

The proposal alone proved no new mechanism; the linked report records implementation evidence.
Implementation remains unstaged for owner review; no commit, push or merge is included.
