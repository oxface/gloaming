# M2 follow-up: inbox dead-letter inspection and retry

Date: 2026-10-21. Branch: `feat/inbox-failure-recovery`, continuing checkpoint `f7d38d4`.
The owner selected completion before merge and approved the
[concrete public interface](../plans/m2-inbox-dead-letters.md) before implementation.
Changes remain unstaged and uncommitted; the starting empty index is preserved.
Earlier [recovery](m2-inbox-failure-recovery.md) and
[supervision](m2-inbox-failure-supervision.md) reports retain their dated evidence.

## Outcome and ownership

One context-typed IInboxDeadLetters replaces the unreleased IInboxRecovery interface.
ListAsync provides bounded live metadata pages, GetAsync inspects one current dead letter,
and RetryAsync retains the existing revision-checked native transition. Rendering adopts
the store through authenticated list/detail/retry endpoints. Consumer/module contracts gain
no business behavior or dependency on operator management.

Public failure policy now returns InboxFailureDecision.DeadLetter; the retained record exposes
DeadLetteredAt. The original `quarantined_at` column, historical migrations, payload, identity,
deduplication and revision/count protocol are preserved. Three forward migrations add only a
partial lookup index; they do not rewrite rows, rename columns or change grants.

The new reusable read mechanism concentrates current-state selection, typed metadata projection,
bounded timestamp/UUID paging and scoped cursor validation. It deepens the already present
recovery operation instead of adding a second retry abstraction or management runtime.

| Owner | Mechanism or policy |
| --- | --- |
| Messaging EF contract | One list/detail/retry interface, detached metadata/query/page declarations and dead-letter terminology. No payload, tenant, tracing, cause/correlation or exception text appears in results. |
| PostgreSQL implementation | Typed no-tracking reads, short owned ReadCommitted transactions, exact lane/code selection, native timestamp/UUID keyset comparison, bounded page size and versioned cursor validation. Existing native retry locking/revision predicate remains. |
| Consumer | Authentication/authorization before each operation, permitted subscription/producer, admitted tenant/RLS context and credentials, classification/reasons/budgets, repair and restart. A cursor grants no authority. |
| Rendering sample | Configured operator credential/permission, fixed `rendering.jobs`/`exports` lane, native HTTP query binding, 400/404/204/409 responses and existing attributed retry logging. |
| Restricted Sales sample | No operator registration by default. Test-only operator grants explicitly permit retry; SELECT-only credentials can inspect without gaining mutation authority. |
| Templates | No template, preset or mandatory messaging/operator setup changed. |

## Fresh proof results

Nineteen new cases were executed: fourteen library cases, two HTTP cases, one checkpoint
migration case and two restricted Sales cases. Existing accounting-competition and HTTP
authorization tests also gained assertions for the new read routes. These are new executions
of active code, not inherited or archived evidence.

| Guarantee | Executed evidence |
| --- | --- |
| Bounded deterministic paging | Five deliveries share one dead-letter timestamp and have UUIDs that exercise database ordering. Two-item pages return each matching delivery once, including the last item; page-size changes retain continuation. Distinct subscription/producer lanes and case-sensitive code filters remain separate. |
| Cursor/input handling | Invalid sizes, blank codes, oversized codes/tokens, malformed/null/unsupported cursor objects and lane/filter mismatch reject before attempting an unavailable database connection. Continuations use the last returned item, not the lookahead item. |
| Metadata-only reads | Command interception observes actual PostgreSQL read SQL without payload selection or row-lock claims. Returned metadata excludes tenant/diagnostic/payload fields, leaves no tracked entities or open transaction, and hides pending/completed/redriven/missing/foreign-lane rows. |
| Live state and stale inspection | Inspection returns the committed dead letter during an uncommitted retry without waiting on its row lock. After commit it disappears. A subsequent failure advances revision/counts; retry with the earlier inspected revision cannot reopen it. |
| Accounting competition | The existing real PostgreSQL advisory-lock barrier pauses failure accounting. New List/Get reads observe no uncommitted dead-letter state while a separate processor still completes unrelated work. Existing lock-retention, competing-operator and obsolete-processor proofs continue to pass. |
| Disappearing rows/restart | A page cursor is obtained, all matching rows are retried, and a fresh provider safely receives an empty continuation. A subsequent failed processing attempt reappears as a dead letter. Existing child-process restart and repeated redrive/deduplication suites pass using the renamed store. |
| Transaction/fresh-context rules | List/Get reject dirty contexts, existing native transactions and ambient transactions. Requested cancellation throws. Reads never save caller business proposals. |
| Consumer HTTP | Authorized one-item pages discover the fixed lane, return typed safe metadata and ignore attempts to select another producer through unrelated URL fields. Detail becomes 404 after retry; re-failure makes the old revision stale. Invalid paging rejects before unavailable storage. Existing no-credential/absent-route cases now cover listing as well as detail/retry. |
| Restricted roles/RLS | A tenant-admitted Sales store lists/gets only its tenant, while native retry of another tenant returns NotApplied under RLS even with explicit operator UPDATE grants. Its own retry succeeds. The other admitted tenant sees its unchanged row. A SELECT-only role lists/gets but native retry fails with insufficient privilege and leaves revision/state unchanged. |
| Checkpoint migration preservation | Rendering migrates explicitly to the M2 checkpoint, records a real failed delivery, then applies the new index migration. Serialization of every retained field is unchanged; the filtered index exists. Inspection/retry and eventual processing succeed with identity, payload, receipt and lifetime failures retained. |
| Existing consumers | Full inbox/workflow/process suites, restricted Sales, Inventory inbox dispatch, local module delivery and architecture pass. Native Rendering/Inventory/Sales snapshots report no pending model changes. |

The new RLS store is exercised in test-only operator composition; the ordinary restricted
processor host still registers no operator store and retains its existing grant inventory.
EF read filters are honored, but they are not authorization for native RetryAsync. That
operation retains its established reliance on authorized invocation plus database grants/RLS.

## Verification

Fresh relevant runs: **421 passed, zero failed, zero skipped** across ten projects/selections.

| Project / selection | Passed |
| --- | ---: |
| Messaging InboxPostgresTests | 66 |
| Messaging WorkflowPostgresTests | 38 |
| Messaging MessagingTests | 8 |
| Messaging WorkflowTests | 8 |
| InboxDemo.Tests | 23 |
| MessagingWorkerDemo.Tests | 10 |
| Wholesale WorkflowDemo.Tests | 143 |
| Wholesale LocalWorkflowDemo.Tests | 14 |
| Wholesale EventPersistenceDemo.Tests, `*InboxDispatchTests` | 15 |
| ArchitectureTests | 96 |

The initial and final focused DeadLetterTests executions also passed all fourteen cases,
including strengthened checks for distinct metadata revision/lifetime/cycle/redrive values
and retry using the newly inspected revision. Those are included in the full inbox count,
not additional distinct tests. Runs used .NET SDK 10.0.401,
PostgreSQL 18.6, existing disposable RabbitMQ/Collector fixtures and rootless Podman. Use the
same environment/test invocation recorded in the original M2 report. The Inventory selection
adds `--filter-class '*InboxDispatchTests'`.

The active solution builds with zero warnings/errors. Native
`dotnet ef migrations has-pending-model-changes --no-build` reports no changes for all three
contexts. Active solution `dotnet format style` and `dotnet format analyzers` both passed
with `--verify-no-changes --no-restore`. CSharpier checked 851 files with `--include-generated`.
All 489 local links in the changed Markdown documents resolve and their whitespace checks
pass. `git diff --check` passes; the staged binary-diff hash is still the starting empty
index. No archive or template files changed.

Native migration scaffolding required the repository's file-scoped namespaces and static
column-array convention before the build passed. The existing Inventory/Sales snapshot entity
ordering was preserved around the native scaffolded inbox block to keep unrelated diff noise
out of review. Old migrations/designers and archive files were not edited.

## Review-worthy files and remaining limits

Review the EF IInboxDeadLetters/InboxDeadLetter/InboxDeadLetterQuery/InboxDeadLetterPage
declarations and the policy/record renames. In the PostgreSQL package review typed projections,
server-side keyset ordering, private cursor codec, registration and unchanged retry predicate.
The processing SQL keeps its previous shape; only dead-letter decision/parameter naming changes.

Review Rendering's RenderInboxOperations and optional host registration, library DeadLetterTests,
HTTP/migration proofs and SalesReplyDeadLetterTests. New lookup migrations are Rendering
`20261021042518`, Inventory `20261021042521`, and Sales `20261021042524`; each Up creates one
index and Down drops it. Native model snapshots include the new CLR name and index. Current
family/package/sample docs, glossary, ADR 0018, design and roadmap explain supported use.

Paging is a live view, not a multi-call snapshot, global queue ordering, event feed or a
permanent export cursor. Inspection reserves no delivery. Concurrent retry can remove rows;
re-failure can reintroduce them. The observed revision may be stale immediately. There is no
total count, arbitrary search predicate, cross-lane listing, bulk retry or payload access.
Cursor binding covers the explicit lane/code inputs, not a consumer's hidden query-filter or
RLS context. Consumers keep that context consistent across the paging journey.

The unreleased interface/registration/DTO names and sample routes change without aliases.
Existing checkpoint storage is retained; consumers update their CLR callers and apply the
forward lookup index through their own migration process. Index creation is ordinary native
DDL, not a proven online rollout or throughput benchmark. Pre-M2 processors ignoring stopped
state still must not run when consumers depend on dead-letter exclusion.

Dead-letter policy, safe codes, admission, operator authority/audit, monitoring and repair/restart
remain consumer-owned. Accounting faults still stop the optional worker under the existing
supervision contract. Neither this read store nor operator retry counts unrecorded crashes or
restarts a faulted worker. Leases/fencing/crash budgets, raw processing-SQL materialization
refactoring, outbox recovery, retention, discard, editing and schema evolution remain separate.
No console, generic management framework, new package/dependency, template change, stage,
commit, push or merge is included in this follow-up.
