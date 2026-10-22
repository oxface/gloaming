# Local release package proof

Requires .NET SDK 10, Node 24.21 or later in the 24.x line, and a PostgreSQL
connection whose role can create and drop databases. The supplied database is
used only to create/drop a randomly named `gloaming_package_*` database; its
existing tables are not changed. Use a disposable PostgreSQL server.

```sh
npm --prefix tools/release ci
npm --prefix tools/release run check
# Supply the connection through your environment/secret manager, never command arguments.
npm --prefix tools/release run verify
```

`GLOAMING_PACKAGE_TEST_CONNECTION_STRING` is required. Missing configuration fails
before packing; PostgreSQL is never silently skipped. `DOTNET_HOST_PATH` can select
a particular `dotnet` executable; otherwise it must be on `PATH`.

The harness packs the 23 `Gloaming.*` library projects using one unique local
prerelease version. Ordinary `dotnet pack` uses the repository's native SDK
[`Directory.Build.targets`](../../src/Directory.Build.targets) to render each
owning README's relative inline links into GitHub links and reject missing targets
or links escaping the repository. This runs before NuGet collects package files,
including when packing without a build, and requires no Node/npm installation.
The generated README stays under `obj`; the task is not shipped in the packages.
`PackageReadmeSource` can still select an already-rendered external source file.

The harness uses regular packing without that override. It independently predicts
README bytes in TypeScript, extracts archives with the SDK's native MSBuild
`Unzip` task and validates package identity/version, license expression, author,
repository, README bytes, assembly, XML documentation and internal dependency graph.
Native renderer probes also exercise encoded Unicode/space/apostrophe/parenthesis
path segments, both directory separators, directory/anchor/external links and
rejection of missing targets and repository escapes.

Four clean executable consumers are created outside the repository:

- Core actor identity and tenancy: lifecycle errors, independent operation scopes,
  opaque identities, distinct actor kinds, concurrent immutable reads.
- Events serialization and history: retained literal JSON, durable identity,
  native JSON configuration, unknown schema rejection and ordered-range validation.
- PostgreSQL ownership, outbox and audit: consumer-owned mapping and wiring,
  unsaved/uncommitted invisibility, native commit and rollback, failed later save
  after earlier business SQL, fresh-context recovery, tenant filters and foreign
  tracked-write rejection.
- All packages: restore, compile and execute with all 23 packages selected.

Each generated project references exact package versions, restores Gloaming only
from the temporary local feed and uses a fresh package cache. Assets must contain
package dependencies and zero project references. The resolved Gloaming graph must
match only the selected roots and their declared dependency closure. Consumers
contain their own small application code, not copied library implementations.

Commands run without a shell, use bounded execution time and retain redacted logs.
The final `manifest.json` records archive SHA-256 values, package dependencies,
profiles and timestamp. The harness prints its temporary directory and preserves
packages, consumers and evidence for inspection; remove that directory afterward.
It does not publish packages, change package-source configuration, stage files or
change the index. NuGet requires access to nuget.org for third-party dependencies.

This is packaging/adoption evidence. It does not repeat every library guarantee,
start RabbitMQ, prove broker delivery or substitute for release publication,
independent architecture/code review, operational capacity tests or owner sign-off.
The PostgreSQL ownership proof exercises EF filters and tracked-save validation;
it does not claim row-level security against bypass SQL.
