# Tenancy family

Explicit operation tenant selection, with optional native HTTP selection and admission composition.

This directory groups source, documentation and library tests. Each package retains its own
public contract and dependencies; the family is not an umbrella runtime package.

| Package | Responsibility and dependencies |
| --- | --- |
| [Gloaming.Tenancy](Gloaming.Tenancy/README.md) | Package-free selected-tenant/tenantless values, single-assignment operation accessor and explicit tenant requirement. |
| [Gloaming.Tenancy.AspNetCore](Gloaming.Tenancy.AspNetCore/README.md) | References only the core and native ASP.NET Core; optional route/subdomain candidate utilities and post-authorization establishment. |

[Current capabilities, consumer obligations and deferred context](docs/capabilities.md)
explain composition. Follow the selected package's README for explicit setup and errors.
The [executable consumer](../../samples/Wholesale/HttpIdentityDemo/README.md) demonstrates adoption.

Library tests:

- [TenantTests](tests/TenantTests/TenantTests.csproj)
- [TenancyAspNetCoreTests](tests/TenancyAspNetCoreTests/TenancyAspNetCoreTests.csproj)

Run commands from the repository root. Restore/build the active solution, then test a selected
project under this family's tests directory with `dotnet test --project <path> --no-build --no-restore`.
PostgreSQL suites require a supported local container runtime. Repository-wide architecture
checks remain in [tests/ArchitectureTests](../../tests/ArchitectureTests/ArchitectureTests.csproj);
shared PostgreSQL test support remains repository-owned. See [development commands](../../docs/development.md).
