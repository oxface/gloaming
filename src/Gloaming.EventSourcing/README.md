# EventSourcing family

Aggregate bookkeeping, optional native EF writes, bounded event history and explicit single-stream rebuilding.

This directory groups source, documentation and library tests. Each package retains its own
public contract and dependencies; the family is not an umbrella runtime package.

| Package | Responsibility and dependencies |
| --- | --- |
| [Gloaming.EventSourcing](Gloaming.EventSourcing/README.md) | Package-free aggregate contract and optional immutable state/pending-event base. |
| [Gloaming.EventSourcing.EntityFrameworkCore](Gloaming.EventSourcing.EntityFrameworkCore/README.md) | References the core, Events.History, EF Core Relational and DI abstractions; provided IEventStore, bounded EventHistoryReader, independent AggregateRebuilder, optional complete AggregateRegenerator, scoped role registration, stream/envelope mapping, ordered append and one required inline aggregate state/save validation. |

[Current capabilities, consumer obligations and deferred context](docs/capabilities.md)
explain composition. Follow the selected package's README for explicit setup and errors.
The [executable consumer](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/README.md) demonstrates adoption.
Its separate [regeneration command](../../samples/Wholesale/Worker/Maintenance/stock-regeneration.md)
demonstrates finite consumer maintenance, native conflicts, cancellation and restart.

Library tests:

- [EventSourcingTests](tests/EventSourcingTests/EventSourcingTests.csproj)
- [EventSourcingPostgresTests](tests/EventSourcingPostgresTests/EventSourcingPostgresTests.csproj) (real PostgreSQL 18.6, standalone direct JSON consumer)

PostgreSQL adoption proofs also live with [Wholesale](../../samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj) and the [independent raw counter](tests/StorageAdoptionTests/EventStorageDemo.Tests.csproj).

Run a selected project from the repository root using `dotnet test --project <path>`.
The [test guide](../../tests/README.md) lists suite ownership, selection and infrastructure needs;
[development commands](../../docs/development.md) cover native/container setup.
