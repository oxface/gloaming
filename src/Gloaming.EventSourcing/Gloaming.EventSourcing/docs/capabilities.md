# Aggregate capability and deferred context

This document and the package README define current interfaces, ownership and supported limits.

## Supported now

The package-free core supplies IEventSourcedAggregate<TEvent> and optional
EventSourcedAggregate<TState,TEvent>. Its contract carries a GUID identity, captured
ExpectedVersion, proposed Version and ordered pending reference-type facts. Several concrete
fact types can share a family interface or object; no domain-event base or persistence envelope
is required. A consumer may implement the interface without inheriting the base.

The base accepts already loaded state/version with no pending facts. Consumer creation methods
can initialize at zero and apply the opening event immediately. Protected ApplyChanges snapshots
an accepted decision, checks null facts/version arithmetic, evolves a complete candidate and
runs candidate validation once. Only complete success changes accepted state/version/pending.
An empty batch changes nothing. ExpectedVersion remains fixed across decisions in one operation.
Consumers own eligibility, pure immutable reducers, candidate invariants and domain factories.

Argument errors cover invalid identity/version/state pairing or null facts. Version arithmetic
can overflow. Consumer evolution/validation exceptions propagate. The base cannot undo mutable
input changes or external effects; state/facts/reducers must be immutable and effect-free.
Aggregates are operation-owned and must not be used concurrently. Pending facts remain proposed
until the consumer's actual persistence commit. No automatic clearing/retry or durability result.

## Related packages and independence

| Package | Selected responsibility |
| --- | --- |
| EventSourcing | Aggregate proposal/bookkeeping; no EF, JSON, HTTP, tenancy or host dependency. |
| EventSourcing.EntityFrameworkCore | Optional native write store, bounded history reader, append/rebuild/regeneration and required inline aggregate participation. References this core, Events.History, EF Relational and DI abstractions. |
| Events.Serialization | Optional explicit durable name/schema registry and native JSON codec. Used by Wholesale; not required by either EventSourcing package. |
| Events.History | Package-free ordered-range metadata validation. Required by the EF adapter's bounded reader; independent of the aggregate core and JSON serialization. |

The independent counter adopts core plus EF storage with direct JSON and its existing reader.
Wholesale combines all four packages. The core itself does not fetch/replay events or validate
a database observation. Native EF stores validate loaded state against a captured header; other
consumers must supply their own corresponding guarantee. See the
[optional EF setup](../../Gloaming.EventSourcing.EntityFrameworkCore/README.md) and
[the family capability record](../../docs/capabilities.md).

## Deferred directions and limits

There is no general history/reconstitution engine, discovered Apply methods, projection registry,
transaction abstraction, pending reset/rebase, automatic persistence or messaging dispatch.
Encapsulated aggregate reducers and separately reusable pure evolution are consumer choices.
Generic lifecycle extensions require a real caller and failure/recovery proof before selection.

The optional EF package implements independent full replay into its one inline aggregate.
Snapshot-plus-tail and persisted progress are beyond this in-memory contract. Optional secondary/
multi-stream processing now belongs to the separately selected PostgreSQL feed; upcasting and
logical command idempotency belong to their independent selected packages. Their current
contracts are linked from the family capability record. A projected summary and the
aggregate's persisted state are views of the same stream, not additional command aggregates.

Provider-specific committed feed/projection operations are implemented in an independent optional
PostgreSQL module, with no core dependency. The [family folder](../../README.md) groups package documentation and tests; packages retain
independent contracts and dependencies.
