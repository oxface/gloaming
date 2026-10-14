# Transport-independent message envelopes

Package-free .NET 10 incoming/outgoing envelopes, `IMessagePublisher` and internal native
messaging trace/metric instrumentation. No EF, hosting, transport,
tenancy, actor identity or event-sourcing dependency.

```csharp
var message = OutgoingMessage.FromPayload(
    Guid.NewGuid(), "exports.render", "exports.render", 1,
    new RenderExportV1(request.Id, request.Pages), jsonOptions);
```

Consumers choose stable delivery IDs, logical RouteKey values, exact durable names/schema
versions, explicit JSON policy and optional opaque TenantKey/CorrelationId/CausationId. IDs must be nonempty, names/routes
nonblank, schema positive and payload defined/non-null. Optional metadata must be nonblank when supplied.
Accepted strings are preserved. The constructor clones JSON; the source JsonDocument may
then be disposed. Get-only properties prevent mutation through record `with` syntax.

Typed construction returns a non-generic envelope, so payload types stop at the serialization
boundary. The caller supplies JsonSerializerOptions; converters and native type metadata remain
consumer choices. Already serialized JSON or Events.Serialization output uses the constructor.

Implement `IMessagePublisher.PublishAsync(OutgoingMessage,CancellationToken)`. Return only
after the adapter's documented acceptance condition; propagate publication errors and honor
cancellation. A successful return does not mean a receiver's business workflow completed.
Delivery may repeat with the same MessageId. Routes, credentials, transport lifetime and
confirmation policy remain explicit consumer code. No publisher is globally registered.

MessageId identifies one delivery and survives retries. Correlation groups related work;
causation identifies the immediate message/command causing new work. Neither is a replacement
for MessageId or business-operation idempotency. Optional correlation/causation are retained
through storage and publication. A handler's new reply commonly inherits CorrelationId and
uses the incoming MessageId as CausationId; no ambient metadata runtime supplies this automatically.

IncomingMessage adds ProducerKey, assigned by the receiving adapter after validating its
trusted transport binding. Its payload is cloned before the transport document/buffer is
released. It carries no native delivery tag/channel or physical route. A subscription belongs
to receiver registration, not untrusted payload data. Neither producer/tenant metadata nor
constructing an envelope authenticates a producer or admits a tenant.

InboxMessageConflictException identifies delivery identity reused with incompatible retained
content, with SubscriptionKey/ProducerKey/MessageId properties and no payload/tenant details
in its error text. See [the complete contract](../docs/capabilities.md).

See [durable message observability](../docs/observability.md) for optional W3C context, schema upgrades, native
activity/meter names, failure logs and host-owned exporter configuration.
The PostgreSQL operations use core instrumentation through internal friend access and
emit their own optional ILogger failure logs. The core has no logging or OTel dependency.
