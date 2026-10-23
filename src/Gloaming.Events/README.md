# Events family

Two independent event utilities: explicit durable JSON identities and ordered history-range validation.

This directory groups source, documentation and library tests. Each package retains its own
public contract and dependencies; the family is not an umbrella runtime package.

| Package | Responsibility and dependencies |
| --- | --- |
| [Gloaming.Events.Serialization](Gloaming.Events.Serialization/README.md) | Package-free explicit durable name/schema registry, native System.Text.Json codec and opt-in forward payload upcasting. |
| [Gloaming.Events.History](Gloaming.Events.History/README.md) | Package-free selected-range position/time integrity validation; no JSON or codec dependency. |

[Current capabilities, consumer obligations and deferred context](docs/capabilities.md)
explain composition. Follow the selected package's README for explicit setup and errors.
The [executable consumer](samples/EventCodecDemo/README.md) demonstrates adoption.

Library tests:

- [Focused consumer adoption proofs](tests/CodecAdoptionTests/EventCodecDemo.Tests.csproj)

- [EventSerializationTests](tests/EventSerializationTests/EventSerializationTests.csproj)
- [EventHistoryTests](tests/EventHistoryTests/EventHistoryTests.csproj)

Run commands from the repository root. Restore/build the active solution, then test a selected
project under this family's tests directory with `dotnet test --project <path> --no-build --no-restore`.
PostgreSQL suites require a supported local container runtime. Repository-wide architecture
checks remain in [tests/ArchitectureTests](../../tests/ArchitectureTests/ArchitectureTests.csproj);
shared PostgreSQL test support remains repository-owned. See [development commands](../../docs/development.md).
