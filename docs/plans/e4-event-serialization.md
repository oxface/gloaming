# E4 Durable event identity and JSON payload codec

Status: scope approved after E3.7 checkpoint `dc3ac3b`, including the owner-selected
`ModulithFoundry.Events.Serialization` name. Owner-reviewed and checkpointed as `2a49ef3b`;
[the report](../reports/e4-event-serialization.md) records fresh proofs and limits.

## Outcome and extraction case

Introduce a small independently adoptable JSON event codec, immediately exercised by two
consumer-owned event families. Library name: `ModulithFoundry.Events.Serialization`, as
suggested by the owner. `Events` groups event-related capabilities by name; it does not
introduce an umbrella package, shared event base class or dependency between every segment.
Other event-library names and splits remain decisions for their own proven capabilities.
Its responsibility is explicit durable name/version registration, runtime type lookup,
JSON payload serialization/deserialization and bounded decoding failures.

The archive contains two concrete implementations:

| Evidence | Repeated mechanism | Consumer differences |
| --- | --- | --- |
| [Inventory serializer](../../archive/proof-sample/modules/Inventory/Inventory/StockPositions/Persistence/StockPositionEventSerializer.cs) | Type-to-identity and identity-to-type dictionaries, duplicate identity checks, JSON conversion and invalid-payload classification. | Stock events use `JsonRequired`; integrity errors carry stream identity and observed stream version. |
| [Purchasing serializer](../../archive/proof-sample/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderEventSerializer.cs) | The same registry, lookup and JSON conversion responsibilities. | Required constructor parameters and native enum conversion are configured separately; its aggregate has its own integrity errors. |

Both discover attributed event classes by scanning an assembly. Replace that discovery with
explicit registrations. Their shared technical work is a plausible extraction, while event
definitions, JSON contracts, domain validation and stream-specific errors remain consumer-owned.
Deleting the proposed codec should restore repeated registry/dispatch/error code in both
consumers; wrapping `JsonSerializer` alone would not justify the library.

These comparisons are historical source/proof observations. E3.7's 320 passing cases establish
ingress. The report separately records E4's fresh registry/codec and two-family consumer proofs.

## Proposed interface and usage

Use concrete native JSON support rather than an abstract serializer interface or separate
abstractions package. The consumer defines its own event-family interface; no Foundry
domain-event base class is needed.

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
};

var codec = new JsonEventCodec<IStockPositionEvent>(
    options,
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

This excerpt follows the implemented consumer registration; the concrete Inventory recipe
uses property attributes and Purchasing enables constructor requirements. The actual compiled
usage is in [the consumer](../../src/Gloaming.Events/samples/EventCodecDemo/README.md). The public surface,
still subject to implementation review, is:

- `JsonEventCodec<TEvent>`: construct once with native options and explicit registrations;
  `Serialize(TEvent)` and `Deserialize(string eventName, int schemaVersion, JsonElement payload)`.
- `EventRegistration<TEvent>.For<TConcrete>(string eventName, int schemaVersion)`: a typed
  descriptor relating a concrete reference event type to its durable identity.
- `SerializedEvent`: durable event name, positive schema version and native `JsonElement`
  payload, separate from stream/storage metadata.
- `EventDecodingException`: requested event name/version, a bounded failure classification
  and an inner JSON exception when relevant. No payload is added to its message.
- `EventDecodingFailure`: `UnknownEvent = 1`, `InvalidPayload = 2`; 0 is invalid.

Use one immutable registry per family. Construction rejects conflicting name/version pairs,
multiple write identities for one CLR type, blank names, nonpositive versions and unsupported
registration types. Duplicate entries fail rather than silently winning by order. Durable
names compare exactly and ordinally; the codec does not trim, normalize or derive names.

Serialization selects the exact registered runtime type, even when called through the
family interface. An unregistered type fails as a caller/configuration error; it cannot
fall back to a base event or invent an identity. Each registered type has one write identity.

Deserialization accepts only an exact registered name/version pair. An unknown name or
unsupported version produces `UnknownEvent`; neither is skipped or resolved through the
latest known version. A null payload result or native `JsonException` produces
`InvalidPayload`. An undefined `JsonElement` is also invalid payload. Converter/configuration
bugs outside `JsonException` retain their native
exception rather than being disguised as stored-data corruption. Consumers attach stream,
event position and product failure policy at their persistence seam in E5.

## Native JSON contract and ownership

The consumer supplies `JsonSerializerOptions`; no Foundry serializer-options hierarchy is
added. Copy the options and registrations during construction and freeze the codec's options
before first use. Later mutation of the original options/list must not change an established
codec. Native converter/resolver objects are shared by reference; consumers own their
correctness and concurrent-use suitability. Avoid a blanket thread-safety claim for arbitrary
custom converters.

The sample recipe uses camel-case JSON, explicit required fields/constructor requirements
and nullable enforcement. Native converters, enum representation, unmapped-field behavior,
naming, case matching and optional defaults remain editable consumer choices. Changing
those choices can change compatibility; the codec does not claim a schema validator or
complete domain validation. A structurally valid event with an invalid business quantity
remains a consumer domain concern.

Native [required property/constructor support](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties)
distinguishes missing fields from supplied values. Native
[nullable enforcement](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations)
is a separate option and has limits for root values, collection elements and generic
members; it does not make a field required. E4 must not imply broader validation.

Native [polymorphic JSON contracts](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism)
remain useful for consumer payloads. Here the durable name/version is external metadata,
as in the existing persisted envelope, so explicit identity dispatch still needs a registry.
Keep native JSON encoding; add no second discriminator field, custom JSON engine or
mandatory event attributes.

The library targets the current .NET 10 baseline and uses platform `System.Text.Json`.
It declares no package, project or extra framework references: no EF, Npgsql, ASP.NET Core,
DI, ActorIdentity, Tenancy, transport, Aspire or sample Contracts dependencies. Consumers
construct/register the codec explicitly. Calls are synchronous and memory-only; there is
no I/O, transaction, retry, event collection, middleware, hosted worker or generated code.
The caller parses and owns source JSON; it supplies a live `JsonElement` during decoding.
Serialization returns a payload usable after the call without a consumer-owned temporary
document. Storage rows, stream IDs/versions, timestamps and attribution stay outside E4.

## Immediate consumer and placement

Add a finite `src/Gloaming.Events/samples/EventCodecDemo` with only a project reference to the new
library. It contains two small internal example families in separate Inventory/Purchasing
folders and explicitly constructs a codec for each. This standalone adoption proof introduces
no HTTP authentication substitute, EF context, bus, empty module Contracts or DI framework.

Inventory's example decodes retained stock-position opened/received JSON and computes an
independently expected on-hand quantity of 10.125 with the existing item/location/unit data.
Use the archived durable aliases with freshly implemented CLR classes/namespaces, including
`StockPositionReceived` in place of archived `StockReceived`. An explicitly optional receipt
reference can demonstrate that the retained v1 payload remains readable without it.
Inventory owns that field/default decision; the library does not add it to events.

Purchasing's example decodes literal drafted/line-set JSON and computes an independently
expected purchase-order code, currency, line quantity and total. Its original required
draft/line fields remain required. Purchasing has no active production module yet; these
are new consumer fixtures based on the archived payload contract, not claims of previously
retained Purchasing fixture files or an implemented order workflow.

Small consumer-local folds make payload interpretation observable. They are not a generic
aggregate, history validator or append implementation. The intended eventual ownership is
Inventory stock positions and Purchasing purchase orders; E5 integrates the exercised types
and codec into their owning modules as persistence behavior arrives. E4's finite consumer
does not change the current HTTP Inventory availability model or add cross-module calls.

Copy the two necessary Inventory literals into the active consumer's fixtures with provenance;
active build/runtime must not read or reference the archive. Preserve the originals and
checksum manifest. Implemented projects: library, its public-interface tests, finite consumer
and focused consumer tests. Avoid another abstractions/provider project.

## Relevant proofs and review gates

Library tests cross the same interface as consumers and protect these decisions:

1. Conflicting durable identities and ambiguous CLR write registrations fail at construction.
2. Interface-typed serialization emits the explicit alias/version and concrete payload fields;
   retained JSON decodes through a renamed CLR event without a stored CLR type name.
3. Unknown identities and unsupported versions fail closed; they do not use another version.
4. Missing required data and null/wrong-shape payloads reach the documented invalid-payload
   classification through the exercised native options. Deliberately optional data is accepted.
5. Consumer configuration is actually used, and changing source registration/options after
   construction cannot rebind an established codec.

Consumer tests use literal JSON and independently expected quantities/order totals. A
serialize-then-deserialize round trip alone is insufficient compatibility evidence. Do not
retest every native JSON option, constructor/null case or enum converter. No artificial v2,
generic upcaster, reflection descriptor snapshot or custom restored-project graph is needed.

Build/run the standalone consumer without database/messaging references. Extend the existing
ArchUnitNET/declaration checks for the new library's dependency promise rather than creating
another architecture-test mechanism. Add both focused suites and the console to container-free
CI; browser/container lanes retain their existing purpose. Verify affected build/style,
analyzers, formatting, archive integrity and links. Additional runtime reruns need a changed
composition or unresolved concern, not the mere addition of a package-free codec.

Review the proposed public types/ownership first, then implementation line by line with
consumer usage and proofs. Stop expansion at the extraction gate if the shared codec is
only a pass-through or forces domain policy into the library. Report actual tested guarantees,
remaining limits and consumer/template/library findings separately from this proposal.

## Template findings and next boundary

E4 supplies an editable explicit event-registration/JSON-contract recipe through its
consumer. It needs no separate template generator or new host resource. Materialized
template output remains E10. The codec is the sixth implemented technical library, with
fresh bounded reuse proofs and owner-reviewed implementation.

E5 owns stream history, ordered hydration, expected-version append and caller-controlled
persistence. E6 owns required views/repair, and E7 owns reliable delivery. Codec evolution,
multiple read schemas per event, upcasting, async projections, arbitrary serializers,
AOT support and transport payload compatibility require later evidence. Code generation
is not part of this proposal.
