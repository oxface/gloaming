# Local packages and adoption verification

`eng/release/` owns the local NuGet snapshot and package-adoption commands.
The retained C# consumers run against the packed libraries. The
[test map](../../tests/README.md) covers the separate .NET suites.

## Everyday local packages

Requires the pinned .NET 10 SDK and Node 24.21+ in the 24.x line. No database or
NuGet account is needed. From the repository root:

```sh
npm --prefix eng/release run pack
# Or use a persistent feed shared with another checkout:
npm --prefix eng/release run pack -- --output /absolute/path/to/gloaming-feed
```

The pack-only command uses Node built-ins and needs no npm dependency installation.
The default feed is `.artifacts/local-feed` in this checkout. Relative `--output`
paths resolve from the repository root, including when npm runs in `eng/release`.
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

### Distribution boundary

This command supplies local development snapshots. Hosted publication is a separate release operation. Preserve the exact
archives and reported snapshot for a consumer's pinned version; rerunning the command creates
a different version. Repacking a fixed source/version is not a supported byte-reproducibility
guarantee: archive metadata can change locked-restore hashes despite equal entry contents.

A consumer can retain a completed snapshot as a versioned artifact without operating a hosted
feed. Record the source revision, package set, archive hashes and retrieval/access instructions,
then prove locked restore from an initially empty package cache. Retain earlier archives for as long as consumers depend on them.

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

## Native relational complex ownership

The `relational-ownership` profile independently selects the base ownership package and Npgsql,
then runs the [native consumer](../../src/Gloaming.Persistence/tests/Fixtures/RelationalOwnership/README.md)
outside the checkout. Required, nested and optional inline values coexist with JSON. It proves
both registration orders and native save paths, complex-only tracking with automatic detection
disabled, tenant isolation, detached concurrency refusal and rollback. Its additional native
value-shape journey compares DDL and exercises required/nullable record structs, primitive
arrays/lists, primitive JSON collections, converted payload leaves and optional discriminator
markers. Locked restore repeats
into a second empty cache. Exact archives, lock, sources and source-mapped config are retained
under `.artifacts/package-adoption/<version>/relational-ownership-consumer` alongside the full
snapshot and JSON compatibility proof. Applications keep native mappings and own migration
review; no additional guard registration, serializer or save path is required.

## Native JSON ownership

The `json-ownership` profile independently selects the base ownership package and Npgsql,
configures native EF complex JSON values/collections, and exercises sync/async changes after
reload, optional values, string enums, tenant isolation/concurrency and rollback. It repeats
locked restore into a second empty cache. Exact archives, lock, consumer sources and source-mapped
config are retained under `.artifacts/package-adoption/<version>/json-ownership-consumer`.
The [consumer contract](../../src/Gloaming.Persistence/tests/Fixtures/JsonOwnership/README.md)
records native CLR-shape limitations; this is not an application data migration.

## Typed evaluations

Two independent consumer profiles select `Gloaming.Evaluations`: ordinary typed functions
without MAF, and the document-title set against native workflows with/without agents plus
recorded output scoring. The latter repeats locked restore into a second initially empty
cache and executes only scripted chat clients. Its library closure has no MAF/EF/provider
dependency; the workflow consumer explicitly selects its native framework/reporting packages.
No credentials or live model calls are needed for these profiles. The full command still
requires PostgreSQL for the other retained adoption profiles.

## Native agent persistence

The full package proof includes an independent document-editor consumer compiled against the
four optional agent/conversation archives, with zero Gloaming source ProjectReferences.
It reconstructs native graph/agent/context/store across start, refine and acceptance against
PostgreSQL; acceptance makes no model call. Additional profiles restore/compile each package
independently, checking that transcript storage remains MAF-free, history remains workflow-free,
and native lifecycle helpers remain EF-free. The package cache starts empty.

Run the full command below with `GLOAMING_PACKAGE_TEST_CONNECTION_STRING` pointing at a
disposable provisioning server. The native consumer creates/drops its own random database.
Its locked-restore proof retains the generated dependency lock and verifies a second empty
cache against the same exact archives. Retained manifests/logs identify the local package
version and hashes; they are the adoption handoff, not hosted-feed publication.

## Full package adoption proof

Use the pinned SDK and Node.js with a disposable PostgreSQL server. Set
`GLOAMING_PACKAGE_TEST_CONNECTION_STRING` to a role with `CREATEDB` and
`CREATEROLE` authority, or use a disposable superuser. Profiles create their
own databases and, where needed, a restricted runtime login. They remove that
scratch state on exit. `DOTNET_HOST_PATH` can select the SDK executable.

```sh
dotnet tool restore
npm ci --prefix eng/release --ignore-scripts
npm --prefix eng/release run check
npm --prefix eng/release run verify
```

`check` runs TypeScript, Prettier, CSharpier and the tooling tests. Repository
integrity runs it when either package proof is selected. `verify` packs a new
snapshot and validates package metadata, assemblies, XML documentation, README
bytes and internal dependencies. The native
[MSBuild targets](../../src/Directory.Build.targets) render README links during
packing, including when the pack uses existing build outputs.

The [verification profiles](verify.ts) restore and run external C# consumers. Their
owning source records the detailed assertions and supported limits.

| Boundary                                     | Examples                                                                                                                                                                                                                                         |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Package selection and public interfaces      | [Core contexts](consumers/Core.cs), [native HTTP establishment](consumers/HttpEstablishment.cs), [event storage](consumers/EventStorage.cs), [event reactions](consumers/EventReactions.cs), all-package composition.                            |
| Native PostgreSQL composition and deployment | [Ownership and audit](consumers/Postgres.cs), [restricted runtime credentials](consumers/RowSecurity.cs), [finite intents](consumers/DueIntents.cs), [committed feed](consumers/EventFeed.cs), [command receipts](consumers/CommandReceipts.cs). |
| Optional dependencies and relevant recovery  | [Hosting](consumers/Hosting.cs), [saved continuations](consumers/Workflows.cs), [native agent persistence](consumers/AgentPersistence.cs), [typed evaluations](consumers/Evaluations.cs).                                                        |

Both verification commands share [package-proof.ts](package-proof.ts). It validates
the completed snapshot, uses the pinned SDK, configures source mapping and restores
into an empty package cache. Each profile checks its exact Gloaming version and
dependency closure, its feed provenance and the absence of source project references.
The independent .NET suites cover library contracts and the application scenarios
described in the [test map](../../tests/README.md).

The printed temporary directory holds the generated consumers, archives, redacted
logs and a manifest with archive SHA-256 values and selected package closures.
The successful run also retains the completed feed and selected example consumers
under `.artifacts/package-adoption/<version>/`. NuGet can reach nuget.org to
restore third-party dependencies and vulnerability data.

## Native Kafka package profile

Run the independently selected native transport proof with the same SDK/Node requirements
and a Docker-compatible engine (Linux x64 is the tested topology):

```sh
npm --prefix eng/release run verify:kafka
```

No database/broker connection or external account is required. Testcontainers creates Kafka
4.3.1 and PostgreSQL 18.6 from pinned image digests, allocates loopback ports and checks
provider readiness. The runner restarts only its owned broker and disposes its containers
and anonymous volumes on ordinary success or failure. See the
[consumer guide](consumers/Kafka/README.md) for rootless Podman configuration.
The [native proxy suite](tests/KafkaProxy.Tests.csproj) compiles the same proxy source
and checks TCP resets, cancellation during connection setup, relay faults and remapped ports.
It uses loopback sockets and runs as an ordinary Nx test. Run it directly with:

```sh
dotnet test --project eng/release/tests/KafkaProxy.Tests.csproj
```

It never skips unavailable infrastructure. Image/NuGet network access may be needed.
The resource reaper provides cleanup on abrupt process exit when the engine supports it.
The printed directory and log identify the uniquely named proof resources.

This command packs a new snapshot with the same pack-only tool, builds the
[retained Kafka consumer](consumers/Kafka/README.md) outside the checkout and restores its four
Gloaming packages from a source-mapped feed into a fresh isolated cache, with zero
ProjectReferences. The transport uses Confluent.Kafka 2.16.0 and native DI. The disposable
fixture uses Testcontainers for .NET at the repository's existing pin.

The [public recipe](../../modular-monolith-patterns/kafka.md) describes the actual protocol and
limits. Provider/process scenarios include native broker acceptance with lost acknowledgements,
process death before outbox completion and around intake commit, local effect rollback,
retained-content conflicts, durable poison disposition, live classic-group ownership loss,
broker restart, cancellation/native close and an owned literal v1 reader. The database
uncertainty probe uses a native post-commit observer, not PostgreSQL wire interception.

The printed temporary directory retains archive/source hashes, exact package closure,
redacted execution.log, results.json, manifest.json and separate cleanup.json. No credentials
are written to configuration files. The index and staged snapshot are checked before/after.
Source hashes describe the copied inputs used for compilation; a successful manifest is written
only after native outcomes, cleanup and index preservation pass.
The command does not publish remotely, stage files or modify a consuming application.
PostgreSQL is the external service required by the ordinary `verify` command.
