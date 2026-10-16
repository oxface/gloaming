# Persistence family

Optional native EF Core ownership filtering, protected-write validation and PostgreSQL RLS.

This directory groups source, documentation and library tests. Each package retains its own
public contract and dependencies; the family is not an umbrella runtime package.

| Package | Responsibility and dependencies |
| --- | --- |
| [Gloaming.Persistence.EntityFrameworkCore](Gloaming.Persistence.EntityFrameworkCore/README.md) | References shared Gloaming EF integration and EF Core Relational; consumer-selected ownership property, named equality filter and native concurrency predicate. |
| [Gloaming.Persistence.EntityFrameworkCore.Postgres](Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md) | Optional ownership-declared read/write policies, automatic transaction-local tenant context and explicit migration helpers; references ownership, Tenancy core and Npgsql. |

[Current capabilities, consumer obligations and deferred context](docs/capabilities.md)
explain composition. Follow the selected package's README for explicit setup and errors.
The [executable consumer](../../samples/Wholesale/PersistenceDemo/README.md) demonstrates adoption.

Library tests:

- [EntityFrameworkCoreTests](tests/EntityFrameworkCoreTests/EntityFrameworkCoreTests.csproj)
- [PersistenceTests](tests/PersistenceTests/PersistenceTests.csproj)
- [PostgresTests](tests/PostgresTests/PostgresTests.csproj): independent fluent RLS consumer and actual read/write enforcement.

Run commands from the repository root. Restore/build the active solution, then test a selected
project under this family's tests directory with `dotnet test --project <path> --no-build --no-restore`.
PostgreSQL suites require a supported local container runtime. Repository-wide architecture
checks remain in [tests/ArchitectureTests](../../tests/ArchitectureTests/ArchitectureTests.csproj);
shared PostgreSQL test support remains repository-owned. See [development commands](../../docs/development.md).
