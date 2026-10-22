# Queued-message schema compatibility

Date: 2026-10-21. Owner-approved [scope and sample surface](../plans/queued-message-schema-compatibility.md),
implemented on feat/queued-message-schema-compatibility from fetched main `86331de` after PR #41.
The [preparation assessment](queued-message-schema-compatibility-assessment.md) records the original
caller findings separately. The prior outbox, inbox and codec reports remain historical evidence;
the runs below are fresh.

## Outcome and extraction findings

Exports keeps default schema-1 writes and can explicitly select schema 2 using the reviewed
ExportRequestCommands constructor. Schema 2 renames Pages to PageCount with the same export
identity and positive-page business meaning. Configuration accepts exactly 1 or 2 before storage
access. Existing SubmitAsync, expected-version checks, transaction/save/commit ownership and
RenderJob storage remain intact. No migration or native grant change was needed.

Rendering defines its own current command and historical transformation, independently of
producer CLR DTOs. It adopts the existing JsonEventCodec/JsonEventUpcaster for exact alias/version
decoding. Both intake and processing use the same consumer decoder. Codec errors are translated
to the sample's existing InvalidDataException admission/classification boundary with their inner
diagnostic retained. The producer uses ordinary JSON; Messaging packages remain codec-free.

Original schema, payload and business metadata remain in sender/inbox storage and on the wire.
Transformation happens only in memory. Original v1 redelivery therefore still deduplicates;
changing the schema under its delivery ID conflicts even when the decoded requirement is equal.
A new delivery ID retains the existing semantic RenderJob idempotency behavior. Conditional
operator retry can reexecute work after reader replacement, but grants neither compatibility
nor admission and never edits its payload.

No new reusable mechanism was proven. Existing codec routing, envelope ownership, PostgreSQL
intake/processing, leases and conditional dead-letter stores supply the necessary mechanisms.
An additional message codec, marker/base type, queue framework or recovery interface was not
justified. Consumer code retains schema meaning, producer selection, trusted producer/tenant
admission, failure classification, deployment coordination and operator authority.

Library finding: optional existing Serialization semantics match historical message decoding;
library interfaces and runtime implementations are unchanged. Sample finding: publisher, intake
and processing gates all need compatible code before enabling v2 writes. Template finding: no
new reusable setup pattern or template change was established.

## Executable adoption and file scope

With the existing MessagingDemo connection/broker variables pointing to empty disposable
module databases, run:

```bash
dotnet run --project src/Gloaming.Messaging/samples/MessagingDemo/MessagingDemo.csproj -- --schema-compatibility
```

The [finite journey](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingDemo/SchemaCompatibilityJourney.cs) commits v1
submission/intake, closes those scopes, explicitly selects v2, then processes both using fresh
scopes, independent PostgreSQL databases and real RabbitMQ. It checks retained schemas/payloads
and prints message identities plus two local jobs. Reusing the storage rejects before another
submission; it supplies no completed-message replay. Existing default v1 demos/producer API
continue unchanged. No new hosted worker or version-selection environment setting was added.

Review these files first:

- [Writer surface](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo/ExportRequestCommands.cs) and the native
  [HTTP](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo/HttpCommandPublisher.cs)/[RabbitMQ](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingDemo/RabbitMqExportPublisher.cs)
  contract gates.
- [Receiver decoder](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/RenderExportHandler.cs),
  [pure receiver-owned transformation](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/RenderV1ToV2.cs) and
  [receiver-only project dependency](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/InboxDemo.csproj).
- [Literal/retained-message/HTTP/operator proofs](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo.Tests/SchemaCompatibilityTests.cs),
  [broker proofs](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo.Tests/BrokerTests.cs),
  [producer proofs](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo.Tests/SchemaCompatibilityTests.cs) and
  [built-process proofs](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/SchemaCompatibilityProcessTests.cs).
- [Exact project dependencies](../../tests/ArchitectureTests/AdoptionDependencyTests.cs) and
  [assembly independence](../../tests/ArchitectureTests/AssemblyDependencyTests.cs): only InboxDemo
  opts into Serialization; all Messaging layers and OutboxDemo retain their codec prohibition,
  and both samples retain event-store/peer-module isolation.
- [Family contract and rollout limits](../../src/Gloaming.Messaging/docs/schema-compatibility.md),
  three consumer README files, Serialization adoption docs and reconciled root scope/status.

## New proofs

Real PostgreSQL and RabbitMQ, actual Kestrel HTTP and built child processes establish:

- Explicit v1/v2 writer selection commits the independently expected JSON field names. Invalid
  selection rejects against unavailable storage without opening it. V2 rollback preserves the
  unsubmitted draft and an independently captured previously queued v1 envelope.
- HTTP accepts and processes valid v2 while retaining its exact ID/schema/JSON values and
  correlation/causation. Original v1 duplicates deduplicate before/after processing. Same-ID
  v2 conflicts; another delivery of equivalent semantic work retains a second receipt and one job.
- Independent v1 literals survive reader replacement as pending or already completed receipts.
  A frozen base-consumer fixture accepts v1 and rejects v2 before business effects. Its v2 failure
  commits the expected dead letter. After installing the new reader, two operators race the same
  revision: one retries, one returns NotApplied, repeated stale retry stays NotApplied, and the
  original v2 message completes once with lifetime failure/redrive evidence preserved.
- A still-unsupported schema 3 remains a dead letter after operator retry, with the same raw
  envelope and no business job. The existing sender contract negative control now uses schema 3
  because 2 is supported; it retains its original rejection/classification assertions.
- Seven invalid/future HTTP cases return 400 with no receipt/job. Three native broker cases
  reject before intake/ack, then closing/reopening the channel yields native redelivery with the
  same ID/body. The fixture discards only its own rejected message after observing that result.
- Both schemas use real confirmed RabbitMQ publication followed by a PostgreSQL completion
  constraint failure. The typed completion signal retains the original native error/message ID.
  Lease recovery republishes the same ID/schema/payload; completed receiver work does not repeat.
- A process proof seeds historical v1 outgoing and pending incoming JSON independently of current
  DTOs, then commits a new v2 command. It kills a dispatcher after actual publication/blocked
  completion and a processor after its blocked uncommitted job insert. Native backend-exit and
  lock observations establish rollback. Replacement roles and two replacement dispatchers recover
  after actual database-clock lease expiry. Three jobs/receipts complete once; original schemas,
  JSON values and business metadata remain, and only the accepted-but-uncompleted sender claim
  advances to a second attempt. Existing provider competition/fencing proofs pass separately.
- The built CLI executes the two-schema mode successfully and rejects a second run on retained
  storage without additional messages/jobs.

The frozen reader is an explicit fixture of base behavior, not a selectable production mode
or proof of arbitrary historical binaries. The process proof observes the selected native
barriers and actual child exits; it establishes no latency, throughput or global execution cap.

## Fresh verification

All eleven commands from the Messaging CI job, followed by Serialization and architecture,
passed against the final solution build with zero failures or skips:

| Suite | Passed |
| --- | ---: |
| MessagingTests | 8 |
| WorkflowTests | 8 |
| WorkflowPostgresTests | 38 |
| OutboxPostgresTests | 102 |
| InboxPostgresTests | 67 |
| OutboxDemo.Tests | 25 |
| InboxDemo.Tests | 40 |
| MessagingWorkerDemo.Tests | 20 |
| WorkflowDemo.Tests, complete | 150 |
| LocalWorkflowDemo.Tests, complete | 15 |
| EventPersistenceDemo.Tests, DispatchTests | 18 |
| EventSerializationTests | 49 |
| ArchitectureTests | 96 |
| **Total** | **636** |

Twenty-five cases are new: six producer, thirteen receiver, three broker rejection, one added
v2 completion-failure case and two process/CLI cases. Remaining results are fresh regressions,
including restricted grants/RLS, competing claims/operators, stale leases, failure accounting,
completion supervision, retention, RabbitMQ/OTLP, workflow recovery and pure codec behavior.
The complete active solution builds with zero warnings/errors. Full-solution style/analyzers,
CSharpier (905 files including generated source), whitespace/local-link checks and preservation
of the empty review index pass.

Development red runs established old v1-only HTTP admission/publication, RabbitMQ publication
and the original one-version CLI behavior. The writer proof initially could not compile until
the reviewed constructor existed. A nonnullable metadata timestamp assertion was corrected.
Two architecture assertions still describing the pre-adoption receiver were updated to the
exact optional consumer dependency, retaining all core/producer exclusions. These failing runs
are excluded from successful counts.

Reproduce after the active solution build with the repository's Docker-compatible runtime:

```bash
dotnet build ModulithFoundry.slnx --no-restore
dotnet test --project src/Gloaming.Messaging/samples/OutboxDemo.Tests/OutboxDemo.Tests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Messaging/samples/InboxDemo.Tests/InboxDemo.Tests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Events/tests/EventSerializationTests/EventSerializationTests.csproj --no-build --no-restore
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj --no-build --no-restore
dotnet format style ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet format analyzers ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet csharpier check . --include-generated
git diff --check
```

The complete library/workflow regression commands remain in the [Messaging CI job](../../.github/workflows/ci.yml).

## Remaining limits

Deploy compatible publishers, receiving adapters and every processor before selecting v2 writes.
An old worker can still reject v2; one upgraded replica does not coordinate the fleet. Keep v1
support for retained work, redelivery, stale publishers and operator retry. A producer can resume
v1 writes, but rolling readers/publishers back to v1-only code is unsafe while v2 can arrive.
Outbox completion/cleanup does not define a safe compatibility-removal window.

Other contract meaning changes, deployment topologies, transports and JSON policies require
separate evidence. No deployment controller, safe reader downgrade, downcast, payload editor,
completed-message replay, inbox retention/deduplication expiry or lease renewal is supplied.
All changes remain unstaged/uncommitted for owner review; no push or PR was requested.
