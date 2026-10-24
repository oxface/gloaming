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
The [executable consumer](samples/PersistenceDemo/README.md) demonstrates adoption.

Library tests:

- [Focused consumer adoption proofs](tests/OwnershipAdoptionTests/PersistenceDemo.Tests.csproj)
- [EntityFrameworkCoreTests](tests/EntityFrameworkCoreTests/EntityFrameworkCoreTests.csproj)
- [PersistenceTests](tests/PersistenceTests/PersistenceTests.csproj)
- [PostgresTests](tests/PostgresTests/PostgresTests.csproj): independent fluent RLS consumer and actual read/write enforcement.

Run a selected project from the repository root using `dotnet test --project <path>`.
The [test guide](../../tests/README.md) lists suite ownership, selection and infrastructure needs;
[development commands](../../docs/development.md) cover native/container setup.
