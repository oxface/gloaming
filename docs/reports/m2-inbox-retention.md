# M2 processed-inbox retention and deduplication expiry

Date: 2026-10-22. Status: owner approved the
[cleanup and receipt-generation interface/scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/m2-inbox-retention.md) before
implementation. Changes remain unstaged and uncommitted on feat/inbox-completed-retention,
based on fetched origin/main eeaa25b. M2 closure remains pending final change-set review and
explicit owner dispositions below. The earlier completed sub-slices did not close the block.

## Outcome and reusable mechanism

Implemented one finite PostgreSQL cleanup call scoped to an exact subscription/producer lane,
with a strict explicit processed cutoff and batches of 1–1000. It deletes only processed,
non-dead-lettered receipts. Pending, delayed, actively processed, dead-lettered and
retried-but-unprocessed work remains. Cleanup reserves no processing work and changes no
business, outgoing or other-lane records.

**Old delivery can queue and execute again once its receipt is deleted.** Until deletion,
the retained envelope still supplies equivalent-delivery suppression and incompatible-content
evidence, even after the retention cutoff passes. Expiry is actual deletion, not an intake TTL.
Consumer semantic operation IDs/outcomes can protect business effects beyond that lifetime.

The new reusable mechanism is bounded PostgreSQL receipt cleanup plus the correctness changes
needed to compose it with existing intake and operator retry:

- Missing envelope-comparison rows after a conflicting INSERT permit insertion again within
  the caller's existing native ReadCommitted transaction. Three unsuccessful observations
  throw DbUpdateConcurrencyException without acceptance. A hidden duplicate key under RLS
  must not cause an endless loop or a fabricated content conflict.
- Immutable, nonempty database-generated ReceiptId identifies one retained receipt generation.
  The existing IInboxDeadLetters retry requires ReceiptId **and** revision. Deletion followed
  by intake can reset revisions; a saved operator request cannot authorize that later receipt.
- Cleanup matches candidate ReceiptId as well as the full delivery key and repeats the final
  processed/cutoff predicates. Native DELETE locks and native RLS/grants arbitrate changes.

No new recovery interface, queue framework, permanent worker or library dependency is added.
The outbox cleanup implementation is unchanged. It shares the finite-call/observed-commit
pattern, but its sender history and lease semantics are different. Existing inbox private
storage mapping/command helpers are reused; similarly shaped inbox/outbox SQL is not sufficient
reason for another shared abstraction.

## Executable consumer adoption

[InboxDemo --cleanup](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/README.md#processed-inbox-cleanup) is a separate
finite maintenance role with explicit maintenance connection, UTC-Z cutoff and batch settings.
It verifies the actual restricted login/effective privileges, authorizes rendering.jobs/exports,
requests a thirty-second cancellation deadline, invokes one batch and prints JSON with the
lane, cutoff, batch size and observed deletion count. No API, intake, processing, migrations
or telemetry sampler starts. Existing consumer infrastructure can schedule that command;
duration, repetition and scheduling remain consumer policy.

```csharp
await using var database = RenderDbContext.Create(maintenanceConnection);
int deleted = await database.DeleteProcessedPostgresInboxBatchAsync(
    "rendering.jobs", "exports", processedBefore, batchSize: 100, cancellationToken);
```

Maintenance requires schema USAGE, table DELETE and six metadata SELECT columns; it needs
no payload/business read, INSERT or UPDATE. Intake/processor/operator grants gain no DELETE.
The finite sample rejects owner/superuser/BYPASSRLS and expanded effective privileges. A
table-wide DELETE grant alone cannot enforce safe lifecycle predicates against arbitrary SQL;
the independent native RLS proof supplies that stronger tenant/lane/processed restriction.
No generic database-security certification, policy installer or role reconciler is supplied.

Rendering retains one RenderJob for equivalent requirements under its independent business
key even after receipt deletion/redelivery. The library proof separately observes a counter
advancing twice across expiry without such a semantic guard. These are intentionally different
consumer outcomes, not an exactly-once claim.

Rendering's existing authenticated retry request now carries expectedReceiptId and
expectedRevision; missing/empty receipt tokens reject with 400. Stale/mismatched tokens return
409. All active inbox retry callers/examples were updated, including native worker recovery,
schema compatibility and restricted Sales. Old revision-only binaries must stop before cleanup.

## Schema, compatibility and review-worthy files

| Review surface | Change and obligation |
| --- | --- |
| [Cleanup extension](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxRetentionExtensions.cs) | Exact lane/cutoff/batch guards, fresh context, observed commit and generation/lifecycle rechecks. |
| [Intake](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInbox.cs) | Handles deleted comparison evidence; bounded visibility observations preserve native caller transaction ownership. |
| [Existing retry interface](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/IInboxDeadLetters.cs), [metadata](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxDeadLetter.cs) and [implementation](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxDeadLetters.cs) | Required receipt-generation condition; no unsafe revision-only overload. |
| [EF record](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxMessageRecord.cs), mapping and native storage validation | Immutable receipt generation; failure counts remain observations within that retained generation. Delivery key/wire fields remain intact. |
| [Rendering migration](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/Migrations/20261022092922_AddInboxReceiptGeneration.cs) | Backfill receipt_id, enforce nonempty UUID and opt into a partial processed-row cleanup index. |
| [Inventory migration](../../samples/Wholesale/modules/Inventory/Inventory/Migrations/20261022093022_AddInboxReceiptGeneration.cs) and [Sales migration](../../samples/Wholesale/modules/Sales/Sales/Migrations/20261022093117_AddInboxReceiptGeneration.cs) | Same receipt-generation backfill/constraint; no unselected cleanup or mandatory retention index. Snapshots retain existing declaration order. |
| [Finite maintenance](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/RenderInboxRetention.cs) and [CLI](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/Program.cs) | Checked separate credentials, explicit policy inputs and one-batch execution. |
| [HTTP operator](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/RenderInboxOperations.cs) | Requires and logs receipt generation alongside observed revision under existing authorization. |
| [PostgreSQL proofs](../../src/Gloaming.Messaging/tests/InboxPostgresTests/RetentionTests.cs), [contract cases](../../src/Gloaming.Messaging/tests/InboxPostgresTests/RetentionContractTests.cs), [races](../../src/Gloaming.Messaging/tests/InboxPostgresTests/RetentionConcurrencyTests.cs) and [authority](../../src/Gloaming.Messaging/tests/InboxPostgresTests/RetentionAuthorityTests.cs) | Real storage/effects, observable lock races, generation reuse and native privilege/RLS controls. |
| [Receiver upgrade](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo.Tests/RetentionMigrationTests.cs), [module upgrades](../../samples/Wholesale/IntegrationTests/Reservations/Broker/OutboxMigrationTests.cs) and [process proofs](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/InboxRetentionProcessTests.cs) | Historical row preservation, actual CLI operation and killed-cleaner rollback/restart. |
| [Family contract](../../src/Gloaming.Messaging/docs/inbox-retention.md), package/sample guides and [ADR 0020](../adr/0020-inbox-expiry-and-receipt-generations.md) | Consumer setup, expiry/authority semantics, migration/deployment ordering and the chosen generation trade-off. |

All three forward migrations backfill nonnull UUID receipt_id using gen_random_uuid() and
preserve original keys/envelopes/recovery state. They take native migration locks and backfill
existing rows; no online rollout is certified. Apply migrations and explicit receipt_id SELECT
for column-restricted inspection/retry roles before upgraded binaries. Existing full inbox
SELECT suffices; no sequence privilege or dispatcher-grant change follows.

Upgrade every intake/operator binary/caller before authorizing cleanup. Earlier intake has
the deletion race; earlier operator SQL ignores generation. Mixed operation with cleanup is
unsupported. Stopping future cleanup cannot restore deleted evidence or establish a safe
binary downgrade. These are consumer deployment obligations, not a supplied fleet controller.

## Fresh proof findings

The new cases use real PostgreSQL 18.6, independent consumer storage and actual child
processes. Trigger/advisory-lock barriers and pg_stat_activity establish the relevant waits;
starting two tasks alone is not claimed as race evidence.

| Obligation | New observation |
| --- | --- |
| Eligibility and batching | Strict processed cutoff including equality/UTC offset, exact subscription/producer, UUID ties and finite repeated batches. Other lifecycle states and lanes remain. Invalid batch/configuration/transaction/cancellation cases reject. |
| Intake versus deletion | Pause after conflicting INSERT, remove the processed winner, and observe Queued rather than false conflict. Intake waiting on an uncommitted DELETE observes committed expiry or rolled-back retained evidence; changed retained content still conflicts. |
| Competing cleaners | Observe first cleaner's advisory wait and second cleaner's row-lock wait on the same batch. After release one deletes and the other returns zero; unrelated pending processing progresses. Later cleanup removes only the remaining eligible batch. |
| Processing versus cleanup | Hold actual completion SQL after local save. Cleanup preserves the active receipt and uncommitted effects. Successful completion later becomes eligible; rejected completion rolls effects back, records failure and remains unprocessed/ineligible. |
| Operator retry versus cleanup | Hold retry SQL while the row is dead-lettered, then commit it pending. Cleanup removes neither state. Existing same-generation/revision competing-operator tests remain and now pass both tokens. |
| Old request against recreated key | Dead-letter, observe, retry/process/delete, then receive the same key and dead-letter at the same revision. Old ReceiptId/revision remains NotApplied; new tokens apply once and repeated requests do not reapply. |
| Expiry and business effects | Passing the cutoff alone still deduplicates/conflicts. After deletion both original and changed-envelope variants can queue under current admission and cause a second local effect. Rendering separately retains its single business job. |
| Rollback and ambiguous commit | Deferred native constraint rejection leaves receipts intact. Real successful commit followed by simulated response loss leaves deletion applied. Fresh calls observe storage; no compensation or cumulative-total guarantee is asserted. |
| Restricted authority | Six-column metadata SELECT plus DELETE succeeds without UPDATE/payload/business access. Native RLS restricts completed lane/tenant rows despite an unrelated EF filter; delivery roles cannot delete. Inspector without receipt_id SELECT fails until explicit grant; inspection alone still cannot retry. |
| RLS-hidden duplicate | An admitted alpha arrival conflicts with an existing beta key hidden by SELECT policy. Intake fails after bounded observations without acceptance or fabricated content conflict; the original receipt remains. |
| Schema upgrade | Rendering, Sales and Inventory preserve pending/completed/dead-lettered raw rows while assigning distinct nonempty receipt generations; models match snapshots. Earlier receiver lookup-upgrade proof now uses an independent pre-generation SQL fixture rather than running the current binary against an incompatible old schema. |
| Executable role/restart | Actual --cleanup child process rejects owner/expanded credentials, deletes one bounded batch and exits. Kill during blocked DELETE, await backend rollback, restart, and observe the retained evidence removed only by successful cleanup. |

Failures observed during development, then corrected and rerun: the original false-conflict
race; old revision-only retry applying to a later receipt; absent migration/CLI adoption;
and an initially unbounded missing-row loop under a hidden conflicting key. Initial fixture
SQL escaping and generated-source style issues were corrected before passing runs. Those red
runs are not counted as current failures or additional guarantees.

There are **36 new test cases**: 26 native inbox cases, three receiver/adoption/migration
cases, five finite-command/process cases and two Sales/Inventory upgrade cases. Existing
proofs were updated for required receipt tokens without dropping their original assertions.

## Verification

The full solution builds with zero warnings/errors. All **770 tests passed**, zero failed
or skipped, in the final source verification. This includes the exact eleven Messaging CI
commands plus Serialization/Architecture and affected persistence/CQRS consumers:

| Suite | Passed |
| --- | ---: |
| MessagingTests | 8 |
| WorkflowTests | 8 |
| WorkflowPostgresTests | 38 |
| OutboxPostgresTests | 102 |
| InboxPostgresTests | 93 |
| OutboxDemo.Tests | 25 |
| InboxDemo.Tests | 43 |
| MessagingWorkerDemo.Tests | 25 |
| Wholesale WorkflowDemo.Tests | 152 |
| LocalWorkflowDemo.Tests | 15 |
| EventPersistenceDemo.Tests DispatchTests | 18 |
| EventSerializationTests | 49 |
| ArchitectureTests | 96 |
| PersistenceDemo.Tests | 36 |
| CqrsDemo.Tests | 62 |

Final checks also passed:

```bash
dotnet format style ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet format analyzers ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet csharpier check . --include-generated
git diff --check
```

CSharpier checked 919 files. Local links and whitespace in changed/new Markdown files were
verified, and the review index remains the original empty snapshot.
Earlier focused red/green executions are not added to this aggregate. Historical M2 reports
and the previous 636-test compatibility result remain prior evidence, not new cleanup proofs.

Reproduce from the repository root using a Docker-compatible PostgreSQL/RabbitMQ runtime:

```bash
dotnet build ModulithFoundry.slnx --no-restore
dotnet test --project src/Gloaming.Messaging/tests/InboxPostgresTests/InboxPostgresTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Messaging/samples/InboxDemo.Tests/InboxDemo.Tests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-build --no-restore
```

The other final suite commands use the same no-build/no-restore invocation on their named
projects; EventPersistenceDemo.Tests uses `--filter-class '*DispatchTests'` exactly as the
Messaging CI lane. Structured results and per-suite logs for this execution are retained at
`/home/oxface/.cache/tmp/m2-inbox-retention-checks-sslnxs7a/`; they are local verification
artifacts, not a new durable repository harness.

## M2 closure review and remaining limits

Implementation/proofs resolve the selected v1 correctness gaps: explicit inbox evidence
expiry, protected active/dead-lettered work, intake under deletion and RLS-hidden visibility,
operator authority across recreated receipts, separate maintenance privileges and consumer
schema/caller adoption. Existing failure accounting/supervision, inspection/retry, restricted
dispatch, outbox completion uncertainty, outbox cleanup and chosen schema evolution remain.

The following **recommendations still require explicit owner disposition**; implementation
approval was not treated as approval to defer them:

| Proposed post-v1 supplied feature | Functional v1 path and consequence of deferral |
| --- | --- |
| Dead-letter discard/purge | Failed work remains isolated, inspectable and retryable after repair; it does not block unrelated work. Retained dead letters can keep growing. No automatic terminal deletion is implemented. |
| Permanent inbox duplicate registry/tombstones | Suppression is explicit while receipts remain. Consumers needing longer-lived effect protection retain semantic operation evidence/constraints, or do not expire those receipts. No permanent delivery-suppression guarantee is claimed. |
| Automatic maintenance scheduling | Callable cleanup and a finite executable command can be scheduled with existing consumer infrastructure. Consumer policy already owns duration/scheduling; the library supplies no scheduler. |
| Durable cumulative cleanup accounting | Observed per-call results and consumer logs provide the operating path. Missing commit responses are ambiguous, so no durable total can be inferred by summing successful outputs. |
| Operator console/inbox dashboards | Existing authenticated metadata inspection/retry and native logs provide the basic operating path. UI, readiness/alerts and consumer supervision remain separately composed. |

A system can use these supported mechanisms without those supplied conveniences, provided
it accepts the documented evidence lifetime and retained-dead-letter policy. If permanent
delivery suppression or terminal disposition is a concrete v1 requirement, resolve that
requirement in M2 rather than declare closure through this report.

Global crash-inclusive execution caps, external exactly-once effects, payload editing and
completed-message replay are not supplied guarantees. Lease renewal, dispatch pools/order,
other providers/transports, unrelated projection work and template expansion are not added.
No throughput/capacity, fleet coordination, safe schema-retirement deadline or automatic
downgrade guarantee is established. New reusable library work is proven as described above;
duration/scheduling, authority, semantic business rules and broader rollout remain consumer policy.

No template files were changed and no new generic template setup pattern was proven. All
changes remain unstaged; exact complete change-set approval is required before committing.
