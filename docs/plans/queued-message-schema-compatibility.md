# Queued-message schema compatibility

Status: scope and sample surface owner-approved, 2026-10-21. The owner selected the next Messaging
continuation after merging PR #41. Fetched base: origin/main `86331de`; working branch:
feat/queued-message-schema-compatibility. The implemented consumer change is prepared for review
with a [fresh slice report](../reports/queued-message-schema-compatibility.md). No Gloaming public
interface was added.

## Outcome and current evidence

Deploy an upgraded Exports/Rendering consumer that can dispatch, admit and process retained
v1 work alongside explicitly selected v2 writes. Both represent the same rendering requirement.
At the reviewed base ExportRequestCommands wrote exports.render schema 1; HttpCommandPublisher,
RabbitMqExportPublisher and RenderExportHandler explicitly admitted only schema 1. The handler
validates again during processing, so upgrading intake alone does not upgrade retained work.

The [assessment](../reports/queued-message-schema-compatibility-assessment.md) traces those
callers and separates existing evidence from the proposed PostgreSQL/transport proofs.

## Concrete consumer contract and usage

Keep the exact producer exports, route/message name exports.render and subscription
rendering.jobs. Preserve tenantless admission, delivery IDs and correlation/causation metadata.
The reviewed wire change is a field rename, with the existing positive-page invariant:

| Wire schema | JSON fields | Receiver interpretation |
| --- | --- | --- |
| 1 | ExportRequestId, Pages | Historical page count |
| 2 | ExportRequestId, PageCount | Same page count |

Property names retain the sample's current PascalCase JSON convention. RenderJob keeps its
existing Pages property and storage. This adds no rendering rule or business migration.

Reviewed sample-owned surface:

```csharp
public sealed record RenderExportV2(Guid ExportRequestId, int PageCount);

public sealed class ExportRequestCommands(
    ExportDbContext database,
    IOutbox<ExportDbContext> outbox,
    int renderSchemaVersion = 1)
```

The new constructor argument is immutable writer policy, accepts exactly 1 or 2 and fails
before querying/mutating on invalid configuration. Existing two-argument callers keep v1
writes. SubmitAsync's signature, business version check and caller-owned transaction remain.
Current RenderExportV1 stays available; each newly enqueued message has one selected schema.

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(cancellation);
var commands = new ExportRequestCommands(
    database, new EfOutbox<ExportDbContext>(database), renderSchemaVersion: 2);
var result = await commands.SubmitAsync(
    exportId, expectedVersion: 1, cancellationToken: cancellation);
await database.SaveChangesAsync(cancellation);
await transaction.CommitAsync(cancellation);
```

The finite MessagingDemo adds --schema-compatibility alongside its existing default journey:

```bash
# Use the existing MESSAGING_DEMO_SENDER_CONNECTION_STRING,
# MESSAGING_DEMO_RECEIVER_CONNECTION_STRING and MESSAGING_DEMO_RABBITMQ settings.
dotnet run --project samples/MessagingDemo/MessagingDemo.csproj -- --schema-compatibility
```

Use disposable sender/receiver databases. The journey explicitly sets up storage, retains v1
work, closes its original scopes, selects v2 for a later submission, and delivers/processes
both through real RabbitMQ and independent PostgreSQL transactions. It reports original
message identities/schemas and local outcomes, and verifies retained payloads remain unchanged.
It starts no new long-running worker or cleanup schedule.

## Decoder, transport and recovery boundaries

Rendering independently defines its current RenderCommand and a receiver-local v1-to-v2 JSON
transformation. Adopt the existing Gloaming.Events.Serialization codec in InboxDemo only:

```csharp
private sealed record RenderCommand(Guid ExportRequestId, int PageCount);

var jsonOptions = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };
var codec = new JsonEventCodec<RenderCommand>(jsonOptions,
[
    EventRegistration<RenderCommand>.For<RenderCommand>("exports.render", 2),
],
[
    new RenderV1ToV2(),
]);
```

Alias/version lookup and an explicit pure forward transformation match this existing codec's
semantics. The receiver-local types do not reference the producer's CLR DTOs. No new payload
marker, message codec, queue framework or dispatcher upcasting hook is proposed. Messaging
packages gain no Serialization dependency; the producer remains an ordinary native-JSON adopter.

RenderExportHandler.Decode validates configured producer/name/tenant admission, decodes v1 or
v2 in memory and checks the same business fields. Translate EventDecodingException to the
consumer's existing InvalidDataException boundary, retaining its inner diagnostic. HTTP admission
and RenderInboxFailurePolicy retain their existing invalid-command behavior and authority.
The transformation owns valid JSON lifetimes and must not mutate the caller's element.

Both native publishers explicitly admit schemas 1 and 2 for this route and preserve the stored
schema and payload when sending. Other versions remain unsupported under consumer policy.
RabbitMqRenderReceiver and HTTP intake validate with the upgraded decoder, then pass the
original IncomingMessage to ReceiveAsync. Processing decodes that original envelope again.

In-memory interpretation must never replace a queued/received payload or its SchemaVersion.
Inbox comparison deliberately includes both. Equivalent v1 redelivery still yields
AlreadyReceived after upgrade, including after processing or retry. Reusing that ID for a
v2 representation conflicts even when the decoded business requirement is equal. A separate
delivery ID with equal semantic work can still use the existing RenderJob business key.

Existing List/Get/Retry interfaces supply metadata inspection and revision-conditional retry.
Retry reexecutes the current decoder; it cannot force an unsupported version. A retained
message rejected by a legacy handler can be retried after installing compatible reader code,
with its exact identity, raw payload and schema preserved. No payload repair endpoint is added.

## Deployment and rollback contract

1. Upgrade every affected dispatch publisher, intake adapter and processing handler to admit
   both versions, while producers continue writing v1. Cover independently hosted roles.
2. Verify that the compatible reader/publisher fleet is deployed before selecting v2 writes.
   An old processor can still reject newly retained v2 work; one upgraded replica is insufficient.
3. Retain v1 support for old outbox rows, pending/dead-letter inbox rows, broker redelivery and
   operator retry. Completion or outbox cleanup does not establish that old delivery has ended.
4. A producer rollback may resume v1 writes. Rolling readers/publishers back to v1-only code
   remains unsafe while v2 work can arrive or be retried. A downgrade is not an automatic rewrite.

The proof matrix uses a frozen v1 reader fixture for the incompatible-reader negative control.
Label it as a fixture of the base consumer's behavior; do not add a production legacy-reader
mode or claim certification of arbitrary rolling deployments.

## Reviewed file and dependency scope

| Files | Change |
| --- | --- |
| samples/OutboxDemo/ExportRequestCommands.cs | Add V2 DTO and explicit optional writer-schema selection; preserve existing V1 callers. |
| samples/OutboxDemo/HttpCommandPublisher.cs; samples/MessagingDemo/RabbitMqExportPublisher.cs | Explicitly admit 1 and 2; preserve original wire envelope. |
| samples/InboxDemo/InboxDemo.csproj; RenderExportHandler.cs; new RenderV1ToV2.cs | Consumer-only Serialization reference and receiver-local decoding/transform. |
| samples/MessagingDemo/Program.cs; new SchemaCompatibilityJourney.cs | Opt-in finite executable adoption using existing contexts/transports. |
| samples/OutboxDemo.Tests/new SchemaCompatibilityTests.cs; FailureRecoveryTests.cs | Version selection, unchanged raw rows, unsupported future version and retry classification. The existing negative control's schema 2 becomes valid and must move to a truly unsupported version. |
| samples/InboxDemo.Tests/new SchemaCompatibilityTests.cs; BrokerTests.cs as needed | Independent historical JSON fixtures, old/new reader matrix, HTTP/RabbitMQ admission, original-envelope deduplication, retained/dead-letter processing and conditional retry after reader replacement. |
| samples/MessagingWorkerDemo.Tests/new SchemaCompatibilityProcessTests.cs | Actual dispatch/receive/process roles, retained mixed-version work and kill/restart/redelivery. |
| tests/ArchitectureTests/AdoptionDependencyTests.cs; AssemblyDependencyTests.cs | Admit the receiving sample's exact optional codec dependency; preserve codec-free Messaging/producer and event-store isolation. |
| OutboxDemo, InboxDemo and MessagingDemo README files | Explicit writer selection, executable command and deployment/rollback examples. |
| Messaging README, capabilities and new docs/schema-compatibility.md; Serialization README/capabilities | Proven optional composition and precise limits beside the owning families. |
| Root design/extraction/roadmap, this plan and a separate implementation report | Record reviewed scope, actual outcomes, checks and remaining gaps. |

No table/column/index migration, native grant change, core package reference, template file,
retention change, lease change or archived fixture edit is proposed. A new ADR is warranted
only if implementation exposes a further hard-to-reverse architecture choice.

## Required fresh proofs

- Independently captured v1 outbox and pending inbox JSON survives fresh contexts/restarted
  processes, then dispatches/processes under the new consumer. Compare native stored IDs,
  names, schemas, JSONB values and business metadata before/after; only lifecycle state changes.
- V1 writer with V2 reader and V2 writer with V2 reader complete the same local business work.
  A frozen V1 reader rejects V2 before effects; it does not silently interpret the missing Pages.
- Real HTTP and RabbitMQ preserve both schemas. Unsupported/future schemas and malformed
  payloads have explicit admission/failure outcomes, with no accidental success acknowledgement.
- Equal original-envelope redelivery deduplicates before and after processing. Reusing its ID
  with a different schema/payload conflicts; equal semantic work under a new ID stays idempotent.
- A privileged fixture retains a V2 envelope for an old processing handler: rejection commits
  failure/dead-letter accounting without business work. Replace the reader, use current revision
  to Retry, process once and verify the same raw identity/schema/payload. Stale/repeated retry
  retains the existing conditional outcome, and an unsupported version remains unsupported.
- Transport acceptance followed by sender completion failure repeats the original V1 or V2
  envelope after lease recovery. Fresh receiver intake does not repeat completed local effects.
- Kill/restart actual independently hosted roles with retained mixed-version work. Existing
  fencing, accounting-fault/completion supervision, restricted credentials and observability
  proofs remain passing regressions, with no new worker configuration or recovery interface.

Run the complete Messaging CI command list, optional Serialization tests, relevant architecture
checks, active-solution build, formatting/style/analyzers and local documentation checks.
Report failed development runs separately from passing results. The selected target is one
consumer compatibility outcome; no new reusable mechanism is currently demonstrated.
