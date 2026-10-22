# Repository instructions

Before choosing change scope, staging, preparing a pull request, or committing, read
[the repository workflow](docs/conventions/repository.md).

## Library and sample work

- Before changing C# code, read [the .NET conventions](docs/conventions/dotnet.md).
- Before architecture, dependencies, or public-interface work, read
  [the current design decisions](docs/design.md) and [the extraction plan](docs/plans/library-extraction.md).
- Before changing shared terminology, read [the project glossary](CONTEXT.md). Keep it to
  resolved definitions; implementation choices belong in the design documents.
- Implement one reviewable capability with executable consumer usage and relevant proofs.
  Library interfaces and implementations receive owner review line by line.
- Keep sample business rules and module Contracts out of technical libraries. Establish
  sample module ownership and domain language when the corresponding sample behavior is added.
- Use the [retirement provenance](docs/reports/s1-archive-retirement.md) for historical source.
  Historical plans and instructions do not authorize active implementation. Preserve current
  owned fixtures and migrations; document compatibility repairs with their owning capability.
- Record settled, hard-to-reverse architecture choices in `docs/adr/`. Proposed interfaces
  remain in the extraction plan until reviewed.

## Slice reports

Report the outcome, verification, review-worthy files, and remaining gaps. Include what
was proven or rejected, supporting evidence, extraction candidates versus consumer-owned
policy, and library/template/sample findings. Distinguish new test results from historical
evidence and proposals. Explicitly state when no new reusable mechanism was proven.
