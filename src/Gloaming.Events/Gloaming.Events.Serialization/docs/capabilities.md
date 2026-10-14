# JSON event decoding capabilities

The [README](../README.md) contains consumer setup and public usage. This package depends only
on native System.Text.Json and can be adopted without any event store, history reader or module.

| Supported capability | Consumer obligation and limit |
| --- | --- |
| Exact durable identities | Explicit ordinal alias and positive schema version; one write identity per concrete CLR type, one type per exact pair. Different CLR types may share an alias at different versions. |
| Historical upcasting | Register JsonEventUpcaster steps with the same alias and strictly increasing versions. Each source must reach an exact terminal CLR registration. Duplicate sources, shadowed exact schemas and missing paths fail construction. |
| Chaining and explicit jumps | Construction resolves paths independent of enumeration order. Each declared intermediate source is readable. No inferred step, nearest schema or undeclared version fallback. |
| PassThrough | Explicit identity step for compatible JSON. Native optional defaults supply absent fields. Required fields/nullability remain native; the library neither inserts null properties nor determines compatibility. |
| Current writes | Exact runtime CLR registration determines schema. Serialization never upcasts or chooses a latest version. |
| Native JSON contracts | Options are copied and frozen; converters/resolvers remain shared objects. Required/default/nullability/naming policy and domain meaning are consumer choices. |
| Payload ownership | Source must be live during decoding. Upgrade input and live results are cloned for independent document lifetime. Returning a disposed result is a consumer error. JSON remains immutable; EF tracking is unrelated. |
| Errors | Unknown/future/undeclared identities use UnknownEvent before JSON inspection. Undefined/null upgrade payloads and JsonException use InvalidPayload, retaining the requested stored identity and native JSON inner exception where present. Other programmer/configuration failures propagate. |
| Concurrent calls | Established registry/options/paths do not rebind. Consumer transforms, converters and resolvers must be concurrently usable. Purity/determinism are consumer contracts, not runtime enforcement. |

Upcasting changes interpretation, never stored facts. No alias rename, reverse conversion,
event split/merge, event position/time editing, row migration, stream processing or retry is
provided. Compatible readers must precede new-schema writers during deployment; zero-downtime
rollout is not certified. Shape-compatible upgrades need not rebuild correct inline state.
Changes in projection meaning require a separate consumer rollout/rebuild decision.

Deferred capabilities include generated discovery, alternative serializers, AOT certification,
transport compatibility and schema-migration orchestration. No domain-event bus follows from
this registry. EventSourcing maintenance workers, extra inline views and multi-stream/async
projections remain separate deferred capabilities in their owning family's catalog.

Evidence and adoption:

- [Pure codec proofs](../../tests/EventSerializationTests/UpcastingTests.cs): explicit routing, optional defaults, strict fields, original-identity errors and JSON lifetime ownership.
- [Standalone chained/nested adopter](../../../../samples/Wholesale/EventCodecDemo/Purchasing/PurchaseOrderSchemaEvolutionExample.cs) and [literal proofs](../../../../samples/Wholesale/EventCodecDemo.Tests/UpcastingCompatibilityTests.cs).
- [Inventory receipt upgrade](../../../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/ReceiptV1ToV2.cs) and [PostgreSQL mixed-schema/rebuild proofs](../../../../samples/Wholesale/EventPersistenceDemo.Tests/AppendTests.Upcasting.cs).
- [Reviewed scope](../../../../docs/plans/event-payload-upcasting.md) and [dated slice report](../../../../docs/reports/event-payload-upcasting.md), separating new results from historical evidence.
