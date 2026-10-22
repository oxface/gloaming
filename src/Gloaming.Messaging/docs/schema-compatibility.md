# Retained message schemas and consumer deployment

Queued messages can outlive their producer, transport adapter and processing-handler deployment.
Keep durable message name/schema identities explicit. Messaging stores and forwards the original
envelope; consumers own compatibility, decoding, admission and rollout. There is no automatic
queued-payload upgrade or required serializer dependency.

## Executable adoption

The [Exports/Rendering composition](../../../samples/MessagingDemo/README.md#schema-compatibility-journey)
demonstrates one shape-only evolution, using two independent PostgreSQL databases and RabbitMQ:

| Contract | Original JSON fields | Local business requirement |
| --- | --- | --- |
| exports.render schema 1 | ExportRequestId, Pages | Positive page count |
| exports.render schema 2 | ExportRequestId, PageCount | Same positive page count |

Producer exports, route/name exports.render and receiving subscription rendering.jobs stay fixed.
The receiver's RenderJob still stores Pages. Wire DTOs remain local to each consumer.

```csharp
// Existing callers keep writing schema 1. Select schema 2 only after upgrading consumers.
var commands = new ExportRequestCommands(database,
    new EfOutbox<ExportDbContext>(database), renderSchemaVersion: 2);
await using var transaction = await database.Database.BeginTransactionAsync(cancellation);
await commands.SubmitAsync(exportId, expectedVersion: 1, cancellationToken: cancellation);
await database.SaveChangesAsync(cancellation);
await transaction.CommitAsync(cancellation);
```

This is sample application policy. The constructor accepts exactly 1 or 2 before touching storage;
IOutbox, dispatch/processing options and the lifecycle protocol gain no version-selection API.
OutboxDemo uses ordinary native JSON and has no codec/event-store dependency.

Rendering independently opts into existing Gloaming.Events.Serialization:

```csharp
var codec = new JsonEventCodec<RenderCommand>(jsonOptions,
[
    EventRegistration<RenderCommand>.For<RenderCommand>("exports.render", 2),
],
[
    new RenderV1ToV2(),
]);
var command = codec.Deserialize(message.MessageName, message.SchemaVersion, message.Payload);
```

RenderCommand is receiver-local, with ExportRequestId and PageCount. RenderV1ToV2 interprets
the historical Pages field through a pure JSON transformation. Both versions retain required
fields and the existing positive-count rule. The handler translates EventDecodingException
into its consumer-owned InvalidDataException boundary, preserving the inner diagnostic.
Unsupported versions and malformed fields therefore follow the existing admission/failure policy.
The codec supplies no transport acknowledgement, transaction, failure policy or operator authority.

The [actual decoder](../../../samples/InboxDemo/RenderExportHandler.cs) is used both before
intake and during retained processing. The optional codec belongs only to InboxDemo's dependency
graph; Messaging libraries remain codec-free, and inbox-only use still needs no event store.

## Original envelopes are the deduplication evidence

Decode in memory. Pass the original IncomingMessage to IInbox.ReceiveAsync; never replace its
SchemaVersion or Payload with a normalized representation. Processing likewise receives the
original retained envelope and interprets it without updating those fields.

For the same subscription/producer/message ID:

- Redelivering the original v1 schema/payload returns AlreadyReceived, including after upgrade,
  processing, dead-lettering or operator retry.
- A v2 representation under that ID conflicts even if it describes the same decoded requirement.
  SchemaVersion and JSONB payload remain part of the existing comparison.
- A different delivery ID is independently retained. The sample's business key prevents a second
  job for the same export when its page requirement is equal.

Native publishers admit exactly schemas 1 and 2 for this sample route. They preserve schema,
payload and business identity when sending. JSONB/transport round trips promise JSON values,
not original whitespace/property order. Diagnostic spans can change independently of business
envelope equality, under the existing [observability contract](observability.md).

## Deployment, retry and rollback

1. Deploy compatible dispatch publishers, receiving adapters and processing handlers across all
   affected instances while producers continue writing v1. Intake and processing can be separate
   processes: upgrading only one role leaves another v1 gate in service.
2. Select v2 writes only after the compatible fleet is deployed. An old worker can reject newly
   retained v2 work even if another worker understands it. The sample does not coordinate rollout.
3. Retain v1 support for sender backlog, pending/dead-letter inbox rows, delayed broker deliveries,
   stale publishers and operator retry. Outbox completion/cleanup is not a reader-removal barrier.
4. A producer can resume v1 writes during rollback while compatible readers stay deployed.
   Rolling readers or publishers back to v1-only code is unsafe while v2 work can arrive/retry.
   There is no downcast, durable rewrite or general safe-downgrade promise.

If retained work was dead-lettered by an old handler, deploy the compatible decoder and inspect
current metadata. An authorized receipt-generation/revision-conditional Retry can then make that original work
eligible. It preserves identity/schema/payload, counters and the consumer's authority boundary.
Competing or repeated requests cannot apply the same receipt-generation/revision pair twice. A still-unsupported schema
fails again; retry cannot grant compatibility or bypass handler admission.

HTTP rejects unsupported/invalid input before durable intake or 202. RabbitMQ parsing/admission
fails before intake/ack; closing the channel permits native redelivery of the original body.
Consumers still own rejection, broker dead-letter disposition and restart policy. A published
message followed by sender completion failure can repeat under its original schema/identity;
receiver deduplication remains necessary. See [outbox recovery](outbox-recovery.md).

## Proof scope and remaining limits

The [slice report](../../../docs/reports/queued-message-schema-compatibility.md) records new
literal-fixture, PostgreSQL, HTTP, RabbitMQ and actual child-process evidence, including retained
v1 work, v2 writes, old-reader rejection, retry after reader replacement, killed workers and
completion uncertainty. The finite CLI requires empty disposable module storage and rejects
reuse; it supplies no completed-message replay or maintenance worker.

This is one consumer contract's compatibility proof using existing libraries. Other payload
meaning changes, CLR/native-JSON policies, transports, mixed deployment topologies and rollout
automation require their own proofs. No new reusable mechanism, database migration, queue
framework or template setup pattern was established by that slice. The later
[processed-inbox retention contract](inbox-retention.md) makes receipt expiry explicit:
original-envelope duplicate/content checks last only while the receipt remains; old delivery
can execute again after deletion. Cleanup does not establish a safe historical-schema removal
window. Payload editing and completed-message replay remain unsupported.
