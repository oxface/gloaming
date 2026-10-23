# Repository instructions

Read [the repository workflow](docs/conventions/repository.md) before choosing scope,
staging, preparing a pull request or committing. Preserve the user's staged review snapshot;
make implementation edits in the working tree.

## Library and sample work

- Before changing C# code, read [the .NET conventions](docs/conventions/dotnet.md).
- Before architecture, dependencies or public interfaces, read [the design](docs/design.md)
  and [library design conventions](docs/conventions/library-design.md).
- Before changing shared terminology, read [the glossary](CONTEXT.md). Keep it to resolved
  definitions; implementation decisions belong in design documents and ADRs.
- Deliver one reviewable capability with executable consumer usage and relevant proofs.
  Public library interfaces and implementations receive owner review line by line.
- Keep business rules and module Contracts in their consuming application. Preserve owned
  payload fixtures and migrations; explain compatibility repairs with their owning capability.
- Record settled architecture choices in docs/adr/. Proposed interfaces remain proposals
  until reviewed.
- For application initialization, new modules or durable handoff, start with the
  [modular monolith patterns](modular-monolith-patterns/README.md). These are application
  guidance, not a required Gloaming runtime.

## Documentation and handoff

Public documentation describes current setup, architecture, decisions, guarantees and limits.
Library contracts belong beside their source; application patterns belong in their own folder.
Research, comparisons, plans, execution reports and local work queues belong in ignored
docs/internal/. Consult that directory only when it exists and the current task needs its
working context; published documentation must stand alone without it.

Report the outcome, relevant checks, review-worthy files and remaining gaps. Distinguish
fresh test results from earlier evidence and proposed behavior. Identify reusable mechanisms
and consumer-owned policy. State when a change establishes no new reusable mechanism.
The owner must explicitly approve the exact complete change set before every commit.
