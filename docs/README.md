# Gloaming documentation

Gloaming provides independently selectable .NET libraries. Consumers own their application
composition and infrastructure configuration.

| Read | Purpose |
| --- | --- |
| [Design](design.md) | Library boundaries, native infrastructure ownership and supported composition. |
| [Development](development.md) | Build, local packages, provider tests and repository checks. |
| [Architecture decisions](adr/README.md) | Accepted choices and their consequences. |
| [Repository conventions](conventions/repository.md) | Review, validation and documentation expectations. |
| [.NET conventions](conventions/dotnet.md) | Source conventions used in this repository. |
| [Library design conventions](conventions/library-design.md) | Requirements for a new reusable capability or public interface. |
| [Modular monolith patterns](../modular-monolith-patterns/README.md) | Apply an opinionated application structure to a new or existing consumer repository. |
| [Library families](../README.md) | Package-specific setup, guarantees and supported limits. |
| [Tests](../tests/README.md) | Executable proof owners and commands. |

A library's local contract is authoritative for its public API. The patterns describe how
an application composes selected capabilities; they do not expand library guarantees.
