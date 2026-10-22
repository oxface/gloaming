# I1: Durable inbox intake and transactional local processing

Date: 2026-10-08. Base `beafa4e` (merged O1), branch `codex/durable-inbox`.
The owner approved the [interface and scope](../plans/inbox1-durable-intake-processing.md)
before implementation. The initial implementation, proofs and documentation handoff was
**unstaged** for line-by-line review. Existing staging is preserved during follow-up review;
follow-up edits remain unstaged. No commit, push, deployment or archive amendment accompanies this report.

## Outcome

Gloaming.Messaging now supplies independent retained intake, callable local processing and an
optional sequential fresh-scope worker in its existing three packages. Consumer adapters commit
intake before native transport acknowledgement/HTTP acceptance. Separate processing owns native
begin/save/completion/commit. It holds one PostgreSQL row lock through bounded local work,
including optional outgoing replies, rather than introducing an expiring inbox lease.

A new reusable mechanism was proven: deduplicated retained intake and excluded transactional
processing remove repeated identity comparison, locking, completion and retry orchestration from
both a tenant/event-sourced Inventory handler and an ordinary inbox-only EF Rendering handler.
Business decisions, payload compatibility, tenant admission and transport remain consumer code.
No generic bus, acknowledgement facade, SQL dialect, module discovery or Gloaming unit of work
was introduced. O1's outbox claim/lease/fencing lifecycle remains unchanged.

## Executable adoption and review order

Start with [intake contract](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/IInbox.cs)
and [processing contract](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/IInboxProcessor.cs),
then [handler contract](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/IInboxHandler.cs),
then [intake](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInbox.cs),
[processing](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxProcessor.cs)
and their [private native SQL support](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxStorage.cs).
The [self-contained setup/guarantees](../../src/Gloaming.Messaging/docs/inbox.md) describe explicit
mapping, both save guards, typed/keyed registration and transaction ownership.

- Inventory's [handler](../../samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueInboxHandler.cs)
  admits its configured Organization and invokes existing state-dependent commands. Accepted
  work commits facts, inline aggregate, reply and completion together. Not-found/conflict/shortage
  complete with a consumer-owned decline reply; technical/admission failures remain pending.
  [The --inbox journey](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/InboxJourney.cs) uses native RabbitMQ.
- [InboxDemo](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/README.md) is an ordinary EF/HTTP receiver with no outbox,
  publisher, Events, EventSourcing, context-family or peer implementation dependency. It records
  a RenderJob, not an external rendering effect. Its business job identity handles semantic
  duplication under different delivery IDs; this policy is not inbox deduplication.
- [MessagingDemo](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingDemo/README.md) alone composes both implementations.
  Separate owning databases/contexts communicate through consumer-native RabbitMQ adapters.
  The receiver knows a wire alias/schema, not the sender's implementation DTO. Queue setup,
  publication confirms, channel lifetime and acknowledgement are explicit sample source.

The immutable incoming envelope owns cloned JSON. Optional correlation/causation now persist
through outgoing enqueue, dispatcher reconstruction and both native sample transports. Replies
inherit incoming correlation and identify incoming MessageId as cause. Direct Inventory commands
retain their previous stock-position correlation fallback without inventing causation. Delivery
identity, conversation grouping and business idempotency remain separate concepts.

## New verification

Executions below ran locally against this implementation on 2026-10-08. PostgreSQL and RabbitMQ
fixtures used real containers through the native Podman socket; these are not mocked transaction
or transport claims. Retry recovery tests make retained work eligible explicitly where needed,
without waiting for production retry intervals.

| Check | Result | Evidence scope |
| --- | --- | --- |
| Whole active solution build | Passed; 0 warnings/errors | All included packages, modules, hosts and tests compile. |
| MessagingTests | 7 passed | Includes 2 new incoming clone/metadata/input cases. |
| InboxPostgresTests | 28 passed, all new | Intake, atomic processing, concurrency, recovery, binding and hosting. |
| OutboxPostgresTests | 28 passed | Existing lifecycle proofs plus 2 new metadata mutation guards; dispatch round trip carries metadata. |
| InboxDemo.Tests | 10 passed, all new | Native HTTP, broker, independent database journey and opt-in worker. |
| OutboxDemo.Tests | 5 passed | Existing producer/HTTP adoption with forward metadata migration. |
| EventPersistenceDemo.Tests | 183 passed | Includes 8 new Inventory inbox cases; existing event and outbox checks retained. |
| HttpIdentityDemo.Tests | 97 passed | Existing Inventory/module ingress and persistence composition. |
| PersistenceDemo.Tests | 36 passed | Existing native tenant-owned EF composition. |
| ArchitectureTests | 71 passed | Updated dependency assertions include inbox-only and host-only composition. |
| Native style/analyzer verification | Passed | Active solution, no changes. |
| CSharpier | Passed, 507 files | Includes generated migration source. |
| Template tooling type/format checks | Passed | Existing TypeScript tools. |
| T1 external generated consumers | Passed; 2 builds and 10 tests | Deterministic creation, event/messaging omission, namespace Task, parent SDK pin and fresh PostgreSQL journeys. |
| Archive checksum verification | Passed, 800 originals | Frozen source/fixtures preserved. |
| Git whitespace check | Passed | Changes unstaged. |

Across the test projects above, 565 cases passed, including 50 newly added cases. Earlier focused
runs of the same cases are not additional unique proofs. T1 additionally passed 10 generated-consumer cases; full Aspire/Keycloak browser composition
was not rerun locally.
No abrupt application-process kill, second-provider behavior or production topology is certified.

### Supported failure and concurrency claims

- Intake commit/rollback and pending/completed duplicates; racing same-key inserts observed
  blocked in PostgreSQL, with winning commit and rollback outcomes. Intake requires native
  ReadCommitted, so the separate comparison statement observes the unique insert's winner.
- Conflicting named schema/payload/tenant/correlation/causation fail visibly. JSONB equivalent
  object ordering, whitespace and numeric spellings deduplicate; changed values/array order
  conflict. Different subscription/producer namespaces remain independent.
- Competing processors skip an already locked delivery and handle other work. Local effects,
  reply and completion commit together. Handler failure after an earlier executed EF save,
  SaveChanges constraint failure and completion SQL failure roll back everything.
- Positive retry scheduling leaves other work eligible. Fresh-context recovery and cancellation
  rollback work. A real processing PostgreSQL connection is terminated after an earlier save;
  its lock/effects roll back, a successor completes, and the old attempt cannot undo completion
  or alter its retry metadata.
- Different deliveries touching the same versioned business row still need native consumer
  concurrency predicates: one loses, its reply/completion roll back, and a fresh retry succeeds.
- Native RabbitMQ channel loss after committed intake but before ACK yields a redelivery with
  stable identity and no second job. Pre-commit failure retains no row and does not acknowledge.
  Sender completion failure after confirmed publication repeats delivery; completed intake
  deduplicates it. ACKed intake survives handler failure and recovers without broker redelivery.
- Missing handler/provider/mapping and tracked inbox lifecycle mutations fail visibly. Two
  context types with the same subscription/identity use independent schemas/handlers; inbox-only
  composition resolves without publisher/outbox/global DbContext. Optional workers use fresh
  scopes, retry and stop cooperatively.
- Inventory accepted/refused decisions, reply metadata, transaction failure/recovery and fresh
  admitted tenant scopes pass. Unadmitted retained metadata does not grant business access.

## Ownership, preservation and limits

Library-owned: immutable envelopes; retained deduplication/JSONB comparison; parameterized SQL;
native row exclusion; atomic local save/completion; pending-only retry deferral; model/save guards;
module-typed/keyed registration; optional fresh-scope worker. Constructors inspect EF metadata,
not database readiness. No required dependency on Events or Persistence was added.

Consumer-owned: wire aliases and exact DTOs, decoding/upcasting rollout, trust/admission, business
eligibility and semantic operation IDs, version predicates, topology/permissions/transport lifetime,
ack/reject decisions, outgoing mappings, migrations/setup, runtime scheduling and reconciliation.
Handlers must retain the processor transaction and restrict effects to local database work;
validation is not a sandbox against trusted code manually committing or privileged SQL.

Completed rows remain retained indefinitely. Poison thresholds/dead letters, redrive/retention,
batch/parallel workers, global FIFO, automatic payload upgrades, other DBMS providers and distributed
transactions remain deferred. A lost commit response may be ambiguous. Cooperatively bounded
handlers are required; an inbox row lock cannot make a non-cooperative handler stop. External
exactly-once effects are not promised, and the rendering sample does not render a PDF.

No template messaging preset or change to generated application composition was added. No archived
source, fixtures, previous migrations/designers, default runtime/browser subscription or event
checkpoint was removed. Forward migrations add only inbox/metadata schema; current snapshots
are updated. CI's Messaging lane includes the new suites and both broker classes; EventSourcing
excludes those classes so each runs once. Unrelated CI progress logging/path-filter work remains
outside this slice. [ADR 0010](../adr/0010-durable-inbox-local-processing.md) records the boundary.

O1's report and the archive's processed-receipt inbox are historical evidence, not executions of
this retained-queue protocol. Current claims above come from new or rerun tests against active code.

## Follow-up owner review

Added statement-spacing conventions and blank lines to edited handlers/adapters and native
processing code. CSharpier 1.3.0 preserves the chosen separators; its configuration cannot insert
them automatically. Startup comments explain the disposable validation scope separately from
fresh attempt scopes. Intake comments explain duplicate-only comparison and the separate
ReadCommitted snapshot after a racing insert. Library-local docs distinguish subscription lanes
from producer identities and record competing-worker behavior and scaling limits. No runtime
behavior or interface changed; formatting and whitespace checks passed for the follow-up.

## Exact implementation-review file manifest

All paths are repository-relative; this includes the initial implementation and review follow-up.

```text
.github/workflows/ci.yml
ModulithFoundry.slnx
docs/adr/0009-module-owned-transactional-outbox.md
docs/adr/0010-durable-inbox-local-processing.md
docs/conventions/dotnet.md
docs/design.md
docs/plans/inbox1-durable-intake-processing.md
docs/plans/library-extraction.md
docs/reports/inbox1-durable-intake-processing.md
src/Gloaming.Messaging/samples/InboxDemo.Tests/AdoptionTests.cs
src/Gloaming.Messaging/samples/InboxDemo.Tests/BrokerTests.cs
src/Gloaming.Messaging/samples/InboxDemo.Tests/InboxDemo.Tests.csproj
src/Gloaming.Messaging/samples/InboxDemo/InboxDemo.csproj
src/Gloaming.Messaging/samples/InboxDemo/InboxDemoHost.cs
src/Gloaming.Messaging/samples/InboxDemo/Migrations/20261008213953_InitialRendering.Designer.cs
src/Gloaming.Messaging/samples/InboxDemo/Migrations/20261008213953_InitialRendering.cs
src/Gloaming.Messaging/samples/InboxDemo/Migrations/RenderDbContextModelSnapshot.cs
src/Gloaming.Messaging/samples/InboxDemo/Program.cs
src/Gloaming.Messaging/samples/InboxDemo/README.md
src/Gloaming.Messaging/samples/InboxDemo/RenderDbContext.cs
src/Gloaming.Messaging/samples/InboxDemo/RenderExportHandler.cs
src/Gloaming.Messaging/samples/InboxDemo/RenderJob.cs
src/Gloaming.Messaging/samples/MessagingDemo/MessagingDemo.csproj
src/Gloaming.Messaging/samples/MessagingDemo/MessagingJourney.cs
src/Gloaming.Messaging/samples/MessagingDemo/Program.cs
src/Gloaming.Messaging/samples/MessagingDemo/README.md
src/Gloaming.Messaging/samples/MessagingDemo/RabbitMqExportPublisher.cs
src/Gloaming.Messaging/samples/MessagingDemo/RabbitMqRenderReceiver.cs
src/Gloaming.Messaging/samples/OutboxDemo/HttpCommandPublisher.cs
src/Gloaming.Messaging/samples/OutboxDemo/Migrations/20261008213949_AddMessageMetadata.Designer.cs
src/Gloaming.Messaging/samples/OutboxDemo/Migrations/20261008213949_AddMessageMetadata.cs
src/Gloaming.Messaging/samples/OutboxDemo/Migrations/ExportDbContextModelSnapshot.cs
src/Gloaming.Messaging/samples/OutboxDemo/README.md
samples/Wholesale/IntegrationTests/EventPersistence/InboxDispatchTests.cs
samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/InboxJourney.cs
samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/InventoryRabbitMqPublisher.cs
samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/InventoryRabbitMqReceiver.cs
samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/Program.cs
samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/README.md
samples/Wholesale/modules/Inventory/Inventory.Contracts/IssueStockV1.cs
samples/Wholesale/modules/Inventory/Inventory.Contracts/StockIssueDeclinedV1.cs
samples/Wholesale/modules/Inventory/Inventory/InventoryDbContext.cs
samples/Wholesale/modules/Inventory/Inventory/InventoryRegistration.cs
samples/Wholesale/modules/Inventory/Inventory/Messaging/InventoryMessageContext.cs
samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueInboxHandler.cs
samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueMessageAdmission.cs
samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueMessages.cs
samples/Wholesale/modules/Inventory/Inventory/Migrations/20261008213945_AddDurableInboxAndMessageMetadata.Designer.cs
samples/Wholesale/modules/Inventory/Inventory/Migrations/20261008213945_AddDurableInboxAndMessageMetadata.cs
samples/Wholesale/modules/Inventory/Inventory/Migrations/InventoryDbContextModelSnapshot.cs
samples/Wholesale/modules/README.md
src/Gloaming.Messaging/README.md
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInbox.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxModelExtensions.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxProcessor.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxServiceCollectionExtensions.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxStorage.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/README.md
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/IInbox.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/IInboxHandler.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/IInboxProcessor.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxMessageRecord.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxModelExtensions.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxProcessingOptions.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxServiceCollectionExtensions.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxWorker.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxWorkerOptions.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxWorkerServiceCollectionExtensions.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxMessageRecord.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxModelExtensions.cs
src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/README.md
src/Gloaming.Messaging/Gloaming.Messaging/InboxMessageConflictException.cs
src/Gloaming.Messaging/Gloaming.Messaging/IncomingMessage.cs
src/Gloaming.Messaging/Gloaming.Messaging/OutgoingMessage.cs
src/Gloaming.Messaging/Gloaming.Messaging/README.md
src/Gloaming.Messaging/docs/capabilities.md
src/Gloaming.Messaging/docs/inbox.md
src/Gloaming.Messaging/tests/InboxPostgresTests/HostingTests.cs
src/Gloaming.Messaging/tests/InboxPostgresTests/InboxConsumer.cs
src/Gloaming.Messaging/tests/InboxPostgresTests/InboxPostgresTests.csproj
src/Gloaming.Messaging/tests/InboxPostgresTests/IntakeTests.cs
src/Gloaming.Messaging/tests/InboxPostgresTests/ProcessingTests.cs
src/Gloaming.Messaging/tests/InboxPostgresTests/RegistrationTests.cs
src/Gloaming.Messaging/tests/MessagingTests/MessageTests.cs
src/Gloaming.Messaging/tests/OutboxPostgresTests/DispatchTests.cs
src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxConsumer.cs
src/Gloaming.Messaging/tests/OutboxPostgresTests/ProducerTests.cs
tests/ArchitectureTests/AdoptionDependencyTests.cs
tests/ArchitectureTests/ArchitectureTests.csproj
tests/ArchitectureTests/AssemblyDependencyTests.cs
```
