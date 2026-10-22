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
Its separate [ES3 regeneration command](../../samples/Wholesale/Worker/Maintenance/stock-regeneration.md)
demonstrates finite consumer maintenance, native conflicts, cancellation and restart.

Library tests:

- [EventSourcingTests](tests/EventSourcingTests/EventSourcingTests.csproj)
- [EventSourcingPostgresTests](tests/EventSourcingPostgresTests/EventSourcingPostgresTests.csproj) (real PostgreSQL 18.6, standalone direct JSON consumer)

PostgreSQL adoption proofs also live with [Wholesale](../../samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj) and the [independent raw counter](samples/EventStorageDemo.Tests/EventStorageDemo.Tests.csproj).

Run commands from the repository root. Restore/build the active solution, then test a selected
project under this family's tests directory with `dotnet test --project <path> --no-build --no-restore`.
PostgreSQL suites require a supported local container runtime. Repository-wide architecture
checks remain in [tests/ArchitectureTests](../../tests/ArchitectureTests/ArchitectureTests.csproj);
shared PostgreSQL test support remains repository-owned. See [development commands](../../docs/development.md).
