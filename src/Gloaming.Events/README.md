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

Run a selected project from the repository root using `dotnet test --project <path>`.
The [test guide](../../tests/README.md) lists suite ownership, selection and infrastructure needs;
[development commands](../../docs/development.md) cover native/container setup.
