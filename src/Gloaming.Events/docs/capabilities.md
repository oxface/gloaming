# Events composition and capabilities

Current scope, 2026-10-07. This family guide and the package-local contracts travel with
the source. Root plans/reports retain cross-cutting review and dated execution evidence.

## Supported composition

Serialization writes the exact registered runtime fact type and reads exact durable name/schema
pairs or explicit forward JSON upgrade paths. Registration collisions and incomplete/ambiguous
paths fail at construction; unknown identities and invalid payloads have typed
decoding errors. History validates a materialized, already ordered (afterVersion, throughVersion]
range for contiguous positions, exact bounds and nondecreasing recorded time. It neither selects
rows nor certifies excluded history.

Select only the packages needed by the consumer:

- [Gloaming.Events.Serialization](../Gloaming.Events.Serialization/README.md)
- [Gloaming.Events.History](../Gloaming.Events.History/README.md)

## Consumer obligations

Consumers define event families and schemas, native JSON options, explicit registrations, database
queries and pure domain reconstruction. Wholesale composes both utilities. The EventSourcing EF
adapter references History for its bounded reader; Serialization remains optional, including
for the independent direct-JSON counter. No shared domain-event base,
umbrella package, bus or handler registry is introduced.

The package READMEs specify public types, explicit integration steps, errors and unsupported
configuration. Copying or referencing one package does not automatically install another
family's context or policy. Consumers keep DI lifetimes, host wiring and native dependency
selection visible.

## Deferred context

Generated discovery, AOT, other serializers, snapshots and safe global feeds are deferred.
History itself has no loader; the optional EventSourcing EF adapter provides bounded native
prefix loading. Compatibility needs literal
historical payload tests; persisted progress and concurrency claims need real database proofs. The
EventSourcing capability catalog records related projection/stream directions.

Deferred features need executable contract proofs before support can be claimed. No future
package or interface is implemented by this repository relocation.

The [payload-upcasting slice](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/event-payload-upcasting.md) records the
implemented optional codec contract and its new evidence. Consumers own the transformations;
the library validates/selects paths and supports PassThrough for compatible optional-field
additions. [Package-local capabilities](../Gloaming.Events.Serialization/docs/capabilities.md)
record routing, ownership and rollout limitations. No stored event migration or projector
maintenance follows from decoding compatibility.

[Related event-store capability catalog](../../Gloaming.EventSourcing/docs/capabilities.md) records the persisted-stream and projection directions.
