# ActorIdentity family

Executing actor identity and optional initiator attribution, with an independently selectable ASP.NET Core adapter.

This directory groups source, documentation and library tests. Each package retains its own
public contract and dependencies; the family is not an umbrella runtime package.

| Package | Responsibility and dependencies |
| --- | --- |
| [Gloaming.ActorIdentity](Gloaming.ActorIdentity/README.md) | Package-free identity values, single-assignment operation accessor and explicit identified-actor check. |
| [Gloaming.ActorIdentity.AspNetCore](Gloaming.ActorIdentity.AspNetCore/README.md) | References only the core and native ASP.NET Core; establishes identity after policy authentication and before consumer authorization handlers. |

[Current capabilities, consumer obligations and deferred context](docs/capabilities.md)
explain composition. Follow the selected package's README for explicit setup and errors.
The [executable consumer](../../samples/Wholesale/Api/README.md) demonstrates adoption.

See the [native operation recipe](../Gloaming.Tenancy/docs/composition.md) and its
[cross-family composition proofs](../../tests/ContextCompositionTests/CompositionTests.cs).

Library tests:

- [ActorIdentityTests](tests/ActorIdentityTests/ActorIdentityTests.csproj)
- [ActorIdentityAspNetCoreTests](tests/ActorIdentityAspNetCoreTests/ActorIdentityAspNetCoreTests.csproj)

Run commands from the repository root. Restore/build the active solution, then test a selected
project under this family's tests directory with `dotnet test --project <path> --no-build --no-restore`.
PostgreSQL suites require a supported local container runtime. Repository-wide architecture
checks remain in [tests/ArchitectureTests](../../tests/ArchitectureTests/ArchitectureTests.csproj);
shared PostgreSQL test support remains repository-owned. See [development commands](../../docs/development.md).
