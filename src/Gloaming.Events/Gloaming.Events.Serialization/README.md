# Explicit JSON event serialization

A package-free .NET 10 codec for explicitly registered durable event identities and native
JSON payloads. Adopt it without EF, HTTP, DI, actor identity, tenancy, messaging or the sample
module layout. `Events` is a naming family; there is no umbrella package or shared event base.

## Consumer usage

Define your own family interface and concrete reference event types, then construct one codec
with native `JsonSerializerOptions` and explicit registrations:

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
};
var codec = new JsonEventCodec<IStockPositionEvent>(options,
[
    EventRegistration<IStockPositionEvent>.For<StockPositionOpened>(
        "inventory.stock-position.opened", 1),
    EventRegistration<IStockPositionEvent>.For<StockPositionReceived>(
        "inventory.stock-position.received", 1),
]);

SerializedEvent stored = codec.Serialize(received);
IStockPositionEvent decoded = codec.Deserialize(
    stored.EventName, stored.SchemaVersion, stored.Payload);
```

See the actual [Inventory](../tests/CodecAdoptionTests/Fixtures/Inventory/StockPositionExample.cs)
and [Purchasing](../tests/CodecAdoptionTests/Fixtures/Purchasing/PurchaseOrderExample.cs)
registration and [standalone consumer](../samples/EventCodecDemo/README.md).

## Guarantees and errors

Construction copies registrations into private dictionaries and clones/freezes native JSON
options. Each concrete event type has one write identity per codec; each exact name/version
pair has one type. Conflicts fail with `ArgumentException` instead of depending on order.
Names must be nonblank and versions positive. Accepted names are preserved and compared
ordinally, without normalization. Abstract/interface registrations are rejected.

Serialization dispatches the exact runtime type, including when the value is passed through
its family interface. An unregistered type raises `InvalidOperationException`; no base-type
fallback, CLR-derived alias or additional payload discriminator is supplied. Serialized output
contains its registered name/version and native `JsonElement`. `SerializedEvent` is an ordinary
data record, not a validator for envelopes manually constructed by a consumer.

Deserialization selects an exact registered pair or an explicitly configured upcasting path.
Unknown names and unsupported versions
raise `EventDecodingException` with `UnknownEvent = 1`; null results, undefined payloads and
native `JsonException` failures use `InvalidPayload = 2`. The requested name/version are
available on the exception. The outer message does not include the payload; native inner
JSON exceptions are retained and can carry converter diagnostics. Consumers choose logging
and attach stream/event position or product error context. No event is skipped or implicitly upgraded.

Other converter/configuration exceptions retain their native type. Null arguments are caller
errors. The caller parses source JSON and supplies a live element during decoding; malformed
JSON text, disposed source documents and envelope-field extraction remain caller concerns.
Serialization returns a payload without a caller-owned temporary document to keep alive.

## Consumer choices and limits

Required fields, optional defaults, converters, enum representation, naming and unmapped-field
policy belong to your native options/event contract. The codec does not force the sample's
JSON policy or validate business rules. See native
[required fields/constructor parameters](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties)
and [nullable annotations and their limits](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations).
Both sample families deliberately enforce their required fields in different native ways.

Changing the original options or registration collection cannot rebind an established codec.
Native converter/resolver objects are shared by reference, not deep-cloned; their mutable
state, thread safety and lifetime remain your responsibility. With concurrently usable
converters/resolvers, an established codec supports concurrent calls without rebinding its
registry/options. There is no disposal, operation scope or I/O inside the codec.

Use ordinary construction or your chosen DI lifetime. The codec provides no discovery,
registration middleware, domain-event collection, dispatch, persistence, transaction, retry,
generated application code or stream metadata. Other serialization engines, AOT support and
transport/deployment topologies beyond the demonstrated consumer require separate proofs.

The typed registration factory has one local CA1000 suppression: the family type belongs on
the registration and the method selects its concrete event type. There is no global analyzer
disable or extra factory facade. Review it with [the interface plan](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e4-event-serialization.md)
and [proof report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/e4-event-serialization.md).

## Explicit historical schema upgrades

Register the current CLR event's write schema once, then supply consumer-owned upcasters as
the optional third constructor argument:

```csharp
var codec = new JsonEventCodec<IStockPositionEvent>(options,
[
    EventRegistration<IStockPositionEvent>.For<StockPositionReceived>(
        "inventory.stock-position.received", 2),
],
[
    new ReceiptV1ToV2(),
]);
```

Derive from `JsonEventUpcaster`, passing the durable name and source/target schema versions
to its protected constructor, and override `JsonElement Upcast(JsonElement payload)`.
The Inventory [receipt transformation](../../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/ReceiptV1ToV2.cs)
renames `quantity` to `receivedQuantity` without changing domain facts. The standalone
[Purchasing example](../tests/CodecAdoptionTests/Fixtures/Purchasing/PurchaseOrderSchemaEvolutionExample.cs)
chains a flat v1 through renamed v2 fields into a nested v3 shape. These transformations belong
to their consumers; the codec owns route selection, ordered execution and terminal CLR dispatch.

An additive optional field usually needs no transformation or version bump:
`string? DeliveryReference = null` supplies null when absent under the sample's native options.
Nullable alone need not make a constructor argument optional. If your schema convention requires
a bump, register the current type at v2 and supply
`JsonEventUpcaster.PassThrough("inventory.stock-position.received", 1, 2)`.
This declares compatible JSON; it neither inserts fields nor bypasses required-field validation.

Every step keeps its exact ordinal event name and increases its schema version. Explicit
jumps are allowed. Duplicate sources, a source also registered as an exact CLR schema, and
paths without an exact terminal registration fail construction with `ArgumentException`.
Declared intermediate sources can be decoded directly. There is no nearest-version fallback,
assembly discovery or order-dependent branching. Existing exact registrations for distinct CLR
types at different versions remain valid; a single CLR type still has one write registration.

For an upgrade, the codec clones the live source and each live intermediate element. Upcasters
must return defined, nonnull JSON and keep their result alive until the codec receives it;
returning an already disposed document is a programmer error. `SerializeToElement` is a simple
way to return owned JSON. Cloning owns document lifetime, not EF tracking or JSON mutation.
Transforms must be pure and safe for concurrent use; the codec does not enforce consumer
determinism, converter/transform thread safety or domain semantics.

Undefined/null upgrade input or output and native `JsonException` from a step or terminal
decode use existing `InvalidPayload`. Unknown/future/undeclared identities use `UnknownEvent`
before payload inspection. Failures retain the original requested stored name/version.
Other consumer failures and disposed elements propagate natively. Serialization never invokes
upcasters: the registered runtime type determines the current write identity.

## Durable message payload adoption

The [Rendering consumer](../../Gloaming.Messaging/samples/Delivery/Rendering/RenderExportHandler.cs) also uses this existing
codec for explicit exports.render message schemas. Its receiver-local v1-to-v2 transformation
renames Pages to PageCount without changing the retained message. A concrete command record can
serve as TEvent; no event base, marker interface or event-store dependency is required.

Only the receiving sample references Serialization. Messaging libraries and the native-JSON
producer do not. Intake retains the original schema/payload for duplicate comparison; the
handler decodes again during processing. EventDecodingException is translated into the
consumer's admission/failure-policy boundary. Codec registrations do not grant operator authority
or perform message retry. See the [executable journey](../../Gloaming.Messaging/tests/Fixtures/BrokerJourney/README.md#schema-compatibility-journey)
and [Messaging contract](../../Gloaming.Messaging/docs/schema-compatibility.md) for the selected
PostgreSQL/HTTP/RabbitMQ and deployment/rollback boundaries.

## Event-store composition and deferred direction

Wholesale uses this codec for event-store envelope encoding and explicit historical decoding.
It maps each exact durable name/schema pair back to the concrete registered CLR fact shape.
The provided event store does not depend on this package: direct JSON/other explicit encoders
remain possible. No domain-event bus or handler registry follows from these registrations.

Upcasting changes read-time interpretation only. Stored JSONB, event IDs, positions and times
remain untouched. Compatible readers must be deployed before newer-schema writers; older codecs
cannot read those writes. A shape-only upgrade need not rebuild already correct inline state.
Projection-meaning changes need consumer-controlled rollout and maintenance; this codec does
not orchestrate either. There is no reverse conversion, alias rename, split/merge, row migration
or automatic acceptance of historical schemas.

Generated registration, alternative serializers and AOT remain deferred. See the
[package capability record](docs/capabilities.md) for the supported boundary and proof links.
See the [event-store capability context](../../Gloaming.EventSourcing/docs/capabilities.md)
for related persisted-history/projection obligations and separately deferred maintenance work.

The focused [receipt executable](../samples/EventCodecDemo/README.md) reads literal v1/v2
payloads, performs an explicit field rename and writes the current schema. Additional nested
and history recipes are test-owned consumers, not required codec dependencies.
