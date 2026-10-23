# Local packages and adoption verification

## Everyday local packages

Requires the pinned .NET 10 SDK and Node 24.21+ in the 24.x line. No database or
NuGet account is needed. From the repository root:

```sh
npm --prefix tools/release run pack
# Or use a persistent feed shared with another checkout:
npm --prefix tools/release run pack -- --output /absolute/path/to/gloaming-feed
```

The pack-only command uses Node built-ins and needs no npm dependency installation.
The default feed is `.artifacts/local-feed` in this checkout. Relative `--output`
paths resolve from the repository root, including when npm runs in `tools/release`.
`--help` shows usage. `DOTNET_HOST_PATH` can select the `dotnet` executable.

Each invocation packs all Gloaming library projects in Release at one new version
such as `1.0.0-local.1792700000000.g0123abcd`. Both the package version and internal
Gloaming dependency versions use that snapshot. No sample/template projects are
packed. All packages build in temporary output before being copied to the feed;
a build failure leaves existing feed snapshots unchanged. Existing archives are
never overwritten. Ordinary copy failures remove files copied by that attempt;
an interrupted copy can leave an incomplete version, so consume only a snapshot
whose completion was reported. Successful output includes the feed, version and
`gloaming.<version>.json` listing the packages.

The command does not run tests, start services, publish remotely, modify NuGet
configuration or change the Git index. It uses normal SDK restore/build output
under each project's `obj`/`bin`. NuGet.org access may still be needed for
third-party dependencies and vulnerability data; local Gloaming distribution does
not make a clean restore fully offline.

### Consume from another repository

Add a `NuGet.Config` to the consuming repository. This example assumes sibling
application and `gloaming` checkouts; replace the local source with the feed
path printed by the command when your layout differs. Relative source paths are
resolved from the configuration file. Retain any other feeds that application
actually requires.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="gloaming-local" value="../gloaming/.artifacts/local-feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="gloaming-local">
      <package pattern="Gloaming.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Select only the package roots the application needs. Replace the example version
with the newly printed version, including its exact-version brackets:

```xml
<PackageReference Include="Gloaming.Messaging.EntityFrameworkCore.Postgres"
                  Version="[1.0.0-local.1792700000000.g0123abcd]" />
```

Then run normal `dotnet restore` and build/run the consumer. An application using
central package management puts the version in its `PackageVersion` entry instead.
Update its selected Gloaming roots together when taking a new snapshot. Transitive
Gloaming packages restore from the same feed; the more specific `Gloaming.*` mapping
wins over `*`.

Always take a new version after changing the libraries. Overwriting an existing
version can leave a consumer using extracted global-cache bytes. `--force` and
`--no-http-cache` do not evict those packages, and changing only SemVer `+metadata`
does not create a distinct NuGet identity. No cache clearing is needed for a new
snapshot version. A temporary `ProjectReference` can shorten cross-repository
co-development, followed by a package-based adoption check.

References: [local feeds](https://learn.microsoft.com/en-us/nuget/hosting-packages/local-feeds),
[source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping),
[cache behavior](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders)
and [version normalization](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning#normalized-version-numbers).

## Full package adoption proof

Requires .NET SDK 10, Node 24.21 or later in the 24.x line, and a PostgreSQL
connection whose provisioning role can create/drop databases and login roles
(`CREATEDB` and `CREATEROLE`, or a disposable superuser). The RLS profile also grants
its new runtime role access to its own schema/table. The supplied database is used
only to create/drop randomly named scratch databases and, for RLS, a random login;
its existing tables and roles are not changed. Scratch databases and the RLS login
are removed in finally. Use a disposable PostgreSQL server.

```sh
npm --prefix tools/release ci
npm --prefix tools/release run check
# Supply the connection through your environment/secret manager, never command arguments.
npm --prefix tools/release run verify
```

`GLOAMING_PACKAGE_TEST_CONNECTION_STRING` is required. Missing configuration fails
before packing; PostgreSQL is never silently skipped. `DOTNET_HOST_PATH` can select
a particular `dotnet` executable; otherwise it must be on `PATH`.

The harness invokes the same pack-only command for the 22 `Gloaming.*` library
projects, using a unique local prerelease version and a temporary feed. Ordinary
`dotnet pack` uses the repository's native SDK
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

Eleven clean executable consumers are created outside the repository:

- Core actor identity and tenancy: lifecycle errors, independent operation scopes,
  opaque identities, distinct actor kinds, concurrent immutable reads.
- Events serialization and history: retained literal JSON, durable identity,
  native JSON configuration, unknown schema rejection and ordered-range validation.
- Event storage: select EventSourcing.EntityFrameworkCore and Npgsql with consumer-owned
  composite keys, filters, direct JSON and manual save overrides. Prove append visibility,
  sync/async saves, rollback, required inline participation, bounded history, explicit repair,
  retained-fact preservation, a stale-writer conflict and a fresh post-repair append. The
  resolved Gloaming graph contains only EventSourcing, its EF adapter, Events.History and
  Gloaming.EntityFrameworkCore.
- Event reactions: select the EF reaction adapter, native DI and Npgsql; collect tracked,
  scoped request and operation-sink facts. Prove ordered downstream waves before validation,
  sync/async and false-acceptance saves, caller commit/rollback, handler/candidate/SQL failure
  recovery, the invocation ceiling and async-only reaction lane selection. The Gloaming
  dependency closure contains only EventReactions, its EF adapter and Gloaming.EntityFrameworkCore.
- HTTP establishment: independently select the ActorIdentity and Tenancy ASP.NET Core
  adapters and run a native loopback Kestrel host. Protected cookies select the effective
  principal; authorization observes its mapped actor before tenant admission. Prove
  once-only mapping, native challenge/forbid, explicit anonymous/tenantless endpoints,
  failed mapping/admission, concurrent requests and following-scope isolation. The Gloaming
  graph contains only the two context cores and their HTTP adapters. Identity mapping,
  membership decisions, authorization and error responses belong to this consumer.
- Restricted PostgreSQL row security: select the persistence PostgreSQL adapter, native
  DI and Npgsql. Deploy a consumer-owned versioned EF migration with ENABLE/FORCE and
  read/write policies, then use a separate restricted login with audited role/table/policy
  privileges. Prove sync/async transaction setup, reads with EF filters disabled, native
  command/bulk SQL isolation, rejected foreign inserts/ownership changes, savepoint recovery,
  caller commit/rollback and reuse of the same reset-enabled pooled connection. Missing
  tenancy/transactions fail before business work. The Gloaming graph contains only
  Persistence.EntityFrameworkCore, its PostgreSQL adapter, Gloaming.EntityFrameworkCore
  and Tenancy. Credentials, migrations and tenant admission remain consumer-owned.
- PostgreSQL ownership, outbox and audit: consumer-owned mapping and wiring,
  unsaved/uncommitted invisibility, native commit and rollback, failed later save
  after earlier business SQL, fresh-context recovery, tenant filters and foreign
  tracked-write rejection. Its resolved persistence/producer graph excludes Hosting abstractions.
- Optional hosting: independently select the worker package, supply callable roles and run
  inbox/outbox workers in fresh scopes without a database provider.
- Continuations: select the combined EF package and PostgreSQL storage, roll back and restart
  a process, commit a saved reply, recover in a fresh context and deduplicate its outcome;
  preserve repeated saves after false-acceptance saving and reject either suppressed
  process/request proposal through both native save forms.
- Command receipts: independently select native EF/PostgreSQL receipts, use consumer save
  overrides without DI/hosting, commit an effect/result and replay it from a fresh context;
  retain valid repeated saves after false-acceptance saving and reject suppressed unsaved
  receipt proposals in both native save forms.
- All packages: restore, compile and execute with all 22 packages selected.

Each generated project references exact package versions and restores Gloaming only
from the temporary local feed. The profiles share one fresh isolated package cache for
the verification run. Assets must contain
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
The PostgreSQL ownership/outbox/audit profile exercises EF filters and tracked-save
validation. The separate row-security profile proves bypass-SQL isolation for its one
explicitly deployed table and restricted login; it does not certify a consumer's full
schema, credential inventory, membership policy or external transaction pooler.
