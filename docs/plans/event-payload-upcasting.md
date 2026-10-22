# Explicit versioned payload decoding and upcasting

Status: public interface and exact scope owner-approved, including PassThrough, on 2026-10-07.
Implemented against checkpoint 91542a6; complete changes remain unstaged for implementation review.
The [slice report](../reports/event-payload-upcasting.md) records execution evidence and remaining limits.

## Finding and bounded capability

JsonEventCodec currently writes one explicit identity per concrete CLR type and decodes only
exact registered name/schema pairs. Registering the same current CLR type for several historical
schemas conflicts with its write registration. A consumer can decode legacy DTOs itself, but
would repeat routing, chaining and terminal-type dispatch for each event family.

Add explicitly registered, forward-only read-time JSON transformations to the existing optional
Events.Serialization codec. Preserve its exact registration behavior when no transformations
are supplied. A transformation changes payload/schema interpretation, never stored rows, event
identity, stream positions, timestamps or command eligibility. The codec chooses and executes
the declared path; the consumer owns every transformation and its domain meaning.

## Reviewed public surface

```csharp
public abstract class JsonEventUpcaster
{
    protected JsonEventUpcaster(
        string eventName,
        int fromSchemaVersion,
        int toSchemaVersion);

    public string EventName { get; }
    public int FromSchemaVersion { get; }
    public int ToSchemaVersion { get; }

    public static JsonEventUpcaster PassThrough(
        string eventName,
        int fromSchemaVersion,
        int toSchemaVersion);

    public abstract JsonElement Upcast(JsonElement payload);
}

public JsonEventCodec(
    JsonSerializerOptions options,
    IEnumerable<EventRegistration<TEvent>> registrations,
    IEnumerable<JsonEventUpcaster>? upcasters = null);
```

This changes only the codec constructor and adds one non-generic abstract class. Existing
two-argument construction and Serialize/Deserialize signatures remain source-compatible.
There is no delegate-heavy command interface, decoder adapter, graph builder, DI helper or
mandatory event base. EventRegistration, SerializedEvent, EventDecodingException and its enum
retain their current public surface. Every registration still selects its write identity.

PassThrough is a library-provided identity transformation for explicitly compatible schemas;
it avoids a consumer subclass when the JSON needs no change. It returns the supplied element;
the codec owns cloning and terminal decoding as for any other step. Its metadata uses the same
validation and routing rules. There is no general-purpose field editing/default injection API.

The upcaster constructor validates a nonblank ordinal alias, positive source version and strictly
higher target version. Metadata is immutable. The alias is unchanged across a step; alias renames,
event splitting/merging, reverse conversion and stream migration are excluded. An explicit jump
such as v1 to v3 is permitted; the codec never invents an intermediate transformation.

## Consumer usage and semantics

```csharp
var codec = new JsonEventCodec<IStockPositionEvent>(options,
[
    EventRegistration<IStockPositionEvent>.For<StockPositionOpened>(
        "inventory.stock-position.opened", 1),
    EventRegistration<IStockPositionEvent>.For<StockPositionReceived>(
        "inventory.stock-position.received", 2),
    EventRegistration<IStockPositionEvent>.For<StockPositionIssued>(
        "inventory.stock-position.issued", 1),
],
[
    new ReceiptV1ToV2(),
]);

// Decode retained v1 into the current CLR event; write only its registered v2 shape.
var current = codec.Deserialize("inventory.stock-position.received", 1, oldPayload);
var encoded = codec.Serialize(current);
```

Inventory's v1 receipt contains `quantity`; v2 uses `receivedQuantity`. The CLR Quantity property,
quantity precision, delivery reference, decisions and evolution remain unchanged. A native
JsonPropertyName attribute selects the v2 field; a module-owned ReceiptV1ToV2 moves the old field
and preserves every other field. Missing/wrong old fields raise JsonException rather than being
invented or defaulted. The transform returns an independently owned element, for example through
JsonSerializer.SerializeToElement. The current required-field/nullable policy remains native JSON.

The materially different adopter is a standalone codec example with no EF or tenancy: retained
Purchasing line-set v1 (`itemCode`, `quantity`, `unitPrice`) upgrades to a renamed flat v2
(`sku`, `units`, `pricePerUnit`), then a nested v3 (`line` containing the original three field
names). Its current CLR event contains a nested line record. Independent v2/v3 literals establish
expected values; quantity 2.5 and price 12.5 must still yield total 31.25. A replaced line with
quantity 5 still yields 62.50. This exercises chaining and nested required-field decoding, rather
than renaming a second domain type. The existing exact-v1 examples remain intact.

### Additive optional fields

Adding `string? DeliveryReference = null` to a current event already allows retained JSON that
omits that field to decode with null under the sample's native options. Nullable alone is not
necessarily optional: RespectRequiredConstructorParameters still rejects an omitted constructor
argument without a default, and JsonRequired still requires presence. Keep this policy native.
For a compatible additive field, keeping the existing schema version is supported; no upcaster
is needed. Consumers decide whether their durable schema convention requires a version bump.

If current writes instead move to v2, register the current type only at v2 and explicitly permit
v1 decoding with the provided helper:

```csharp
EventRegistration<IStockPositionEvent>.For<StockPositionReceived>(
    "inventory.stock-position.received", 2);
JsonEventUpcaster.PassThrough("inventory.stock-position.received", 1, 2);
```

The helper does not insert a JSON null property or determine compatibility. Terminal native
decoding supplies the declared optional default. Required-field changes, renamed fields and
changes in meaning still need a real transformation or must fail. Future/undeclared schemas
remain rejected; there is no automatic acceptance of all older versions.

Both examples preserve domain facts. Shape-only upgrades do not require rebuilding already
correct inline state. Explicit Inventory rebuilding is exercised to prove compatibility, not
automatically performed on reads. Old deployed codecs cannot decode newly written schemas;
deployment must establish compatible readers before enabling those writers. No rolling-upgrade
or projection-meaning rollout guarantee is introduced.

## Registration, routing and JSON ownership

Construction snapshots registration collections and freezes options as today. It indexes
transformations by exact (eventName, sourceVersion), rejecting duplicate sources. A source cannot
also have an exact CLR registration: otherwise the codec would have two interpretations for it.
Existing exact registrations with distinct CLR types/name-version pairs remain valid.

For each declared source, construction follows strictly increasing versions until an exact CLR
registration is reached. A missing next step or terminal registration fails construction. There
is no fallback to a nearest version. Strict increase prevents cycles; uniqueness prevents
branches from one source. A declared intermediate version is itself an accepted read source.
The codec can precompute these bounded paths; it does not inspect assemblies or event history.

Deserialize resolves the exact terminal or a declared path before touching JSON. An unknown
identity, future schema or undeclared older schema fails. For an upcast path, clone the live input
element, run its ordered steps, validate and clone each result, then deserialize the terminal
payload with the terminal registration's CLR type and native options. The original requested
alias/schema stay on failures. Serialize never runs upcasters or chooses a version automatically.

JsonElement cloning owns document lifetime and does not make JSON mutable. Consumer upcasters
must return a live, nonnull/defined element; returning an already disposed document is a programmer
error which cloning cannot repair. Pure, stateless transformations with no I/O are the consumer
contract. The codec cannot enforce determinism or thread safety of consumer code, just as it
cannot enforce those properties in native converter/resolver instances. Established codecs
support concurrent reads when those dependencies are concurrently usable.

## Errors, dependencies and ownership

| Trigger | Result |
| --- | --- |
| Invalid step alias/version or null arguments | Native argument exceptions |
| Duplicate source, exact-registration shadowing, missing step/terminal | ArgumentException at codec construction; no order-dependent route |
| Unknown alias, future version or undeclared historical version | Existing EventDecodingException / UnknownEvent = 1 |
| Null/undefined payload or JsonException in a transform/terminal decode | Existing EventDecodingException / InvalidPayload = 2, original requested identity and native JsonException inner where applicable |
| Other consumer transform/converter failures, disposed source/result | Native exception propagates; no skipped event, fallback or retry |

No new error enum value or exception metadata is needed. The optional package stays package-free,
with native System.Text.Json only. EventSourcing core/EF and Events.History gain no codec dependency.
The direct-JSON counter remains independent. Consumer event contracts, JSON policy, transformation
logic, tenant admission, reducers, transaction/save/commit and maintenance authorization stay local.

## Exact file and behavior scope

Paths are relative to the repository root. Serialization denotes
src/Gloaming.Events/Gloaming.Events.Serialization.

| File | Approved change |
| --- | --- |
| Serialization/JsonEventUpcaster.cs (new) | Add the reviewed metadata/transform abstraction and PassThrough factory with a private identity implementation. |
| Serialization/JsonEventCodec.cs | Optional upcasters, deterministic registration/path validation, owned JSON transformations and terminal decode; unchanged write dispatch/default exact reads. |
| src/Gloaming.Events/tests/EventSerializationTests/UpcastingTests.cs (new); CodecTests.cs | Native routing/error/ownership/concurrency proofs; preserve exact-registration tests including distinct CLR types under different schemas. |
| samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionEvents.cs | Change only receipt JSON field annotation to receivedQuantity; retain CLR/domain shape. |
| Same StockPositionCodec.cs; ReceiptV1ToV2.cs (new) | Current receipt write schema 2 and explicit module-owned v1 transform; opened/issued remain schema 1. |
| src/Gloaming.Events/samples/EventCodecDemo/SchemaEvolutionJourneys.cs (new); Purchasing/PurchaseOrderSchemaEvolutionExample.cs (new) | Standalone nested-v3/two-step consumer; no new library/EF dependencies or business Contracts. |
| src/Gloaming.Events/samples/EventCodecDemo/Program.cs; README.md | Add opt-in `--schema-evolution` executable journey so existing default demo output remains unchanged. |
| src/Gloaming.Events/samples/EventCodecDemo/Fixtures/Inventory/received.v2.json (new); Purchasing/line-set.v2.json and line-set.v3.json (new); Fixtures/README.md | Independently authored new payload versions; explain provenance and shape-only meaning. All existing v1 JSON stays byte-for-byte intact. |
| src/Gloaming.Events/samples/EventCodecDemo.Tests/UpcastingCompatibilityTests.cs (new) | Exercise old/new literals, nested chaining, preserved decimal values, current writes and opt-in executable output. |
| samples/Wholesale/EventPersistenceDemo.Tests/AppendTests.Upcasting.cs (new); HistoryReadTests.cs and other append/rebuild files only where assertions encode v1 writes | Real PostgreSQL mixed-schema append/read/rebuild/rollback/retained-payload/fresh-context proofs; keep historical fixture decoding and existing outcomes. |
| samples/Wholesale/EventPersistenceDemo/SchemaEvolutionJourney.cs (new); Program.cs; README.md | Opt-in `--schema-evolution` PostgreSQL journey: retained v1 plus new v2, live reads, explicit rebuild/save/commit and unchanged stored historical payload. Existing default journeys/output stay intact. |
| Serialization/README.md; Serialization/docs/capabilities.md (new); Events/README.md; Events/docs/capabilities.md | Self-sufficient supported setup, errors, ownership, limits and deferred context beside the library. |
| EventSourcing/EF README and local/family capabilities; docs/design.md; docs/plans/library-extraction.md; new docs/reports/event-payload-upcasting.md | Record the optional codec composition and new versus historical proof evidence; preserve unsupported maintenance/projection capabilities. |

If a current test asserts a schema that becomes valid, update it to a genuinely unsupported
future schema without weakening its failure outcome. Preserve frozen archive, historical
migrations, existing JSON literals, original default demo outputs, unrelated worktree changes
and T1's event-free composition. No new package, schema migration, stored event update, worker,
queue, snapshot, async/multi-stream projection or template preset.

## Verification contract

Pure codec proofs cover exact/default decoding, current writes, one-/two-step and explicit-jump
paths, registration order independence, duplicate/ambiguous/missing paths, forward-version bounds,
unknown/future versions, strict fields/native options, original-identity errors, invalid intermediate
payloads, propagation of non-JSON failures, source immutability/document lifetime, copied collections
and concurrent use with stateless transformations. Prove additive optional-field defaults with
both an unchanged identity and an explicit PassThrough v1-to-v2 step; required missing fields
must still fail, and new writes must retain their registered v2 identity. Include pass-through
steps within a transformation chain and the same registration/error/ownership contract.
Use independent literals and expected events,
not only serialize/deserialize round trips.

PostgreSQL consumer proofs append a v2 receipt to retained v1 history, compare live and inline
state at the same version, explicitly rebuild missing/corrupt inline state, and compare event IDs,
names, schemas, positions, timestamps and JSONB payload before/after. Failed old-payload upgrades
must leave tracking/storage untouched; failed native repair saves and rollback retain prior state,
and a fresh context can recover. Existing optimistic repair/append concurrency and transaction
suites must still pass. No new locking or scheduling mechanism is selected.

Run relevant existing codec/history/family/consumer/dependency checks, active build/style/analyzers,
archive/fixture integrity and external event-free T1 proof. Record actual new results in a slice
report. Checkpoint/draft inspection is historical evidence; only fresh recorded executions
establish new runtime guarantees. Leave changes unstaged; exact complete
commit approval remains separate.
