# M2 inbox retention: current-code assessment

Date: 2026-10-22. Status: preparation evidence for
[owner interface/scope review](../plans/m2-inbox-retention.md). No C# or schema implementation,
new reusable mechanism or new PostgreSQL test result is claimed here.

## Scope and repository state

The owner explicitly includes inbox retention/deduplication expiry in M2 before v1. Earlier
chat statements that M2 was closed were too narrow. Completed M2 sub-slices remain supported
work; their completion does not dispose of this gap or authorize the proposed interface.

The checkout and index were clean on feat/queued-message-schema-compatibility at aee42d7.
Fetched origin/main is eeaa25b and includes that slice. A clean, untracked-from-base
feat/inbox-completed-retention branch reuses the same checkout from origin/main. Other
worktrees exist; this proposal does not edit them or require a new isolated checkout.
Proposal edits remain unstaged. Commit approval is separate from interface review.

## Findings from actual implementation

- [PostgresInbox](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInbox.cs)
  owns no transaction: callers begin native ReadCommitted, retain intake, then commit before
  accepting transport. A conflicting INSERT is followed by a separate envelope comparison.
  Missing comparison currently throws InboxMessageConflictException, just like incompatible
  content. Allowing cleanup introduces a deletion interval between those two statements.
- [PostgresInboxProcessor](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxProcessor.cs)
  and [storage](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxStorage.cs)
  hold a native row lock before the handler savepoint through effects/completion or failure
  accounting. A processed row is terminal under the supported retained-row lifecycle. There
  is no inbox lease to test for expiry or renew. Only processed receipts are proposed for deletion.
- [PostgresInboxDeadLetters](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxDeadLetters.cs)
  inspects retained metadata and retries by delivery key plus revision. No receipt generation
  currently exists. Recreating a deleted key resets revision and can eventually admit a stale
  operator request against a later receipt. This is a code-derived risk, not a newly executed
  PostgreSQL reproduction; the proposed proof must force equal revisions across generations.
- [Inbox mapping](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxModelExtensions.cs)
  starts recovery counts/revision at zero and has pending and dead-letter indexes, but no
  processed-retention index. Schema migrations belong to Rendering, Inventory and Sales.
- [Completed-outbox cleanup](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxRetentionExtensions.cs)
  has a useful finite call/transaction/error pattern and metadata-only DELETE authority.
  Its lease predicates and sender-history guarantee do not establish inbox expiry semantics.
  Do not broaden outbox retry merely to force matching public shapes: producers already must
  retain unique message IDs, whereas old native inbox delivery can reappear without a new producer action.
- [Rendering](../../src/Gloaming.Messaging/samples/InboxDemo/InboxDemoHost.cs) currently exposes optional authenticated
  list/detail/retry endpoints. Its request carries only ExpectedRevision. Its
  [business handler](../../src/Gloaming.Messaging/samples/InboxDemo/RenderExportHandler.cs) uses a unique RenderJob
  identity, so that sample alone cannot prove absence of repeated handler execution after expiry.
  An independent local-effect proof is needed alongside the consumer business-idempotency proof.
- [Restricted Sales](../../samples/Wholesale/Worker/Broker/sales-inbox-rls.md) already separates
  admitted tenant processing and optional retry grants from intake/global outbox dispatch.
  Receipt generation must migrate without granting DELETE to any of those roles. Native
  SELECT/DELETE policies, not EF filters, govern retention authority.
- Existing intake, failure/retry, dead-letter, retention, migration and native child-process
  proof fixtures can exercise the proposal. PostgreSQL trigger/advisory-lock barriers already
  provide observed race and process-death evidence without production fault hooks.

## Extraction findings and next evidence

Candidate reusable work is one finite PostgreSQL inbox cleanup extension, safe intake under
concurrent deletion, and a receipt-generation condition on the existing retry interface.
These are **proposals**, not mechanisms proven by this assessment. Retention duration,
schedule, selected lane, credentials, semantic operation idempotency and operator authority
remain consumer policy. No general retention/queue/recovery framework or template preset
is justified. No new reusable mechanism was proven during preparation.

The proposal includes exact interface/usage, affected schemas/callers, new PostgreSQL obligations,
current library/sample findings and explicit owner decisions needed for post-v1 gaps. No tests
were run in this preparation step; previous M2 and 636-test schema-compatibility results are
historical evidence. Final implementation checks and M2 closure remain outstanding.
