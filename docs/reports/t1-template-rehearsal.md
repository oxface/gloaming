# T1: bounded template-population rehearsal

2026-10-06. One supported consumer composition, owner-approved and checkpointed as
`8ccf4c8`, including the review corrections recorded below. No package was published.
The event experiment was checkpointed separately as `1ae13d4`; T1 does not remove or extend it.
The existing planning edits and frozen archive are preserved.
At the owner's request, the creation/proof tooling now uses TypeScript and npm.

This checkpoint-status update adds no new test execution. The results below retain their
original execution scope. The next proposed capability is
[ES1 bounded event append](../plans/es1-bounded-event-append.md).

## Outcome and supported configuration

The [creator](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/create.ts) reads explicit JSON containing exactly
`applicationName` and `rootNamespace`, then creates a new consumer-owned repository.
The [creation guide](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/README.md) defines validation, invocation,
atomicity and proof prerequisites. Creation currently supports Linux with glibc and
Node.js 24.21+ (24.x), using npm with
the repository's .NET 10 SDK; the generated application uses ordinary .NET projects.

Application names are non-keyword ASCII C# identifiers, 1–64 characters, excluding
reserved device names. Root namespaces are dotted non-keyword ASCII identifiers, at most
200 characters. Compiler-reserved `__arglist`, `__makeref`, `__reftype` and `__refvalue`
are rejected in both settings. Both values are required strings; duplicate/unknown keys and source tokens
are rejected. There are no provider, authentication, messaging or event-sourcing switches.
The sole composition is a console host plus a state-stored PostgreSQL Catalog module.

Native `dotnet new` was evaluated before implementation against Microsoft's
[authoring reference](https://learn.microsoft.com/en-us/dotnet/core/tools/templates) and
[template configuration reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json).
Its native solution template and symbols suffice for project/path/name/namespace
materialization. A custom replacement language or generation engine was rejected.
The small TypeScript boundary validates configuration, supplies the library payload and
enforces the stricter creation contract.

Node executes erasable TypeScript directly; `tsc --noEmit` checks types separately. The
tooling has its own private npm package and exact lockfile. `jsonc-parser` detects duplicate
JSON keys after strict JSON syntax parsing. `koffi` calls glibc's no-replace rename, preserving
the original race protection that ordinary Node rename would lose. Its platform binary is
installed as an optional dependency with install scripts disabled. `fast-xml-parser`,
TypeScript, Node types and Prettier are proof/check dependencies. None enters the generated
application's source or dependency graph; no frontend workspace or general engine is added.

Generation installs the staged template in a temporary native custom hive, runs no restore
or post-actions, and publishes with Linux `renameat2(RENAME_NOREPLACE)`. Both native
commands run from staging with the template's `global.json`, overriding any output-parent
SDK pin. Template task types use `global::System.Threading.Tasks.Task` so the valid root
namespace `Task` cannot shadow them. The destination
must not exist, even as an empty directory or dangling link; its parent must exist.
Invalid inputs, occupied output, native engine failure and competing creation do not
publish partial output or replace owner files. Repeated creation is refused; customization
updates and force behavior are not implemented. Kill/power loss may leave temporary sibling
staging; filesystem durability across power loss is not claimed. The custom-hive switch
is internal SDK functionality, exercised on 10.0.112; future SDK compatibility needs this proof.

## Generated tree and library distribution

For `applicationName=Cedar`, `rootNamespace=Acme.Cedar`:

```text
Cedar.slnx
global.json / Directory.Build.props / Directory.Packages.props / NuGet.Config
.editorconfig / .gitignore / .config/dotnet-tools.json
README.md / foundry.json / library-sources.json
libraries/
  ModulithFoundry.ActorIdentity/                 existing source + project
  ModulithFoundry.Tenancy/                       existing source + project
  ModulithFoundry.Persistence.EntityFrameworkCore/ existing source + project
src/Cedar.Host/
  Cedar.Host.csproj / Program.cs / Composition.cs / CatalogDesignTimeFactory.cs
modules/Catalog/
  Cedar.Catalog.Contracts/                      ICatalogQueries + CatalogItem
  Cedar.Catalog/                                rows, queries, DbContext, registration
    Migrations/                                InitialCatalog + designer + snapshot
tests/Cedar.Adoption.Tests/                     executable native adoption journey
```

Seven projects adopt only three existing Foundry libraries. Creation copies 23 current
library source/project files byte for byte, preserving their library namespaces and
recording SHA-256 hashes. No library source snapshot is duplicated in the template itself:
the creator materializes the selected source when invoked. Root build/package settings
provide their framework and existing package versions. All source/project references and
restored project graphs stay within the generated repository. NuGet restore is still needed.

Source materialization is the bounded unpublished T1 distribution choice. Local packaging
was unnecessary for this creation proof. It does not settle release/package divisions or
an upgrade policy; consumers must explicitly review future snapshot changes. No permanent
distribution ADR or new library interface was introduced.

## Executable journey and consumer obligations

The default host command prints instructions without connecting or initializing storage.
An explicit `migrate` command requires `CATALOG_CONNECTION_STRING` and uses native EF
`MigrateAsync`; it seeds nothing. Catalog explicitly owns `catalog.items` and migration
history in `catalog.__EFMigrationsHistory`. The included native design-time factory and
pinned EF tool support subsequent consumer-owned migration scaffolding.

The generated adoption suite creates its own database on a disposable PostgreSQL server,
runs the actual host before initialization, checks that no application tables exist,
runs the actual explicit migration command, and checks exact tables and model/migration
consistency. It then seeds Alpha for tenant A and Beta for tenant B through separate DI
scopes, explicit native transactions, saves and commits. Reads call `ICatalogQueries`
through its registered implementation, with independently expected IDs/names. The suite
rejects a foreign insert and a detached update that claims the current tenant while
targeting the foreign row; a fresh read verifies Beta was unchanged.

Four separate missing-context cases use an unreachable database to prove rejection before
storage access: missing actor, missing tenant, anonymous actor and deliberate tenantless
execution. Context establishment, named ownership filtering, validation from both native
save overrides and the stored-owner concurrency predicate use existing library contracts.

Trust fixture identities exist only in tests. The host exposes no business ingress and
adds no fake authentication scheme, actor header or caller-controlled trust path. Before
exposing Catalog, the consumer owns authentication/trusted work, canonical identity mapping,
tenant admission and capability permission, then initializes each required context exactly
once in a fresh operation scope. Presence checks do not authorize work. Catalog's Contracts,
identified-actor requirement and business vocabulary are editable consumer policy.

Consumers also own database provisioning, connection credentials, native provider/schema
setup, migrations, seeds, save/transaction boundaries, cancellation, retries and failure
presentation. Raw/bulk SQL, disabled filters, identity-map access and privileged operations
need consumer safeguards. There is no same-tenant edit-version policy or cross-module
transaction guarantee. DbContext concurrency is not implied by concurrent context reads.

## Fresh verification

The [creation proof](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/verify.ts) is wired into an independent
[CI workflow](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/.github/workflows/template.yml) with PostgreSQL 18.6. It runs from the
checkout but restores, builds, formats and tests consumers with their working directories
under `/tmp`, outside Foundry. CI installs Node 24.21.0, runs `npm ci --ignore-scripts`,
checks TypeScript/Prettier, then runs the npm creation proof. CI hosting itself has not
run locally; those commands have.

| Proof | New result |
| --- | --- |
| Cedar / Acme.Cedar and HarborDesk / Task | Both restore/build with zero warnings/errors; native style/analyzer checks pass; collision-shaped naming is exercised |
| Conflicting output-parent SDK pin | Both consumers are created beneath a strict SDK 9.0.100 pin; staging and consumer pins select the required SDK 10; the parent pin is unchanged |
| Generated adoption suites | 5 passed per consumer, 10 total; none failed/skipped |
| Fresh disposable databases | Explicit migration, exact Catalog tables/history, no implicit initialization or seed, tenant isolation and foreign-write protection pass |
| Determinism | Same configuration yields identical complete file paths/bytes before build/tooling artifacts |
| Configuration failures | 25 invalid JSON/shape/name/namespace/unsupported-option cases refuse without creating output, including all four compiler-reserved tokens in both naming settings, escaped duplicate keys, comments and trailing commas |
| Occupied/repeated output | Empty/nonempty directory, file, dangling link and repeated creation refuse without content changes |
| Other creation failures | Missing parent, early/partial native engine failure leave no destination; two concurrent native creators yield exactly one complete winner; an externally created empty destination survives publication failure |
| Omission/naming/independence | Generated sources, project graph and restored package graph contain no selected event/messaging residue, Wholesale names or checkout references; library hashes match upstream |
| TypeScript tooling | Fresh strict type checking and Prettier pass; the earlier port's clean locked npm install audited 17 packages with zero vulnerabilities and matched the original Python output byte for byte, before these review corrections |
| Active repository solution (original T1 run) | Build passed with zero warnings/errors; native style/analyzers passed; these review corrections change only creation tooling and template task type qualification |
| Existing relevant suites (original T1 run) | ActorIdentity 19, Tenancy 17, EF model/write validation 23, Architecture 65, Context consumer 15: 139 passed, none failed/skipped; these are earlier evidence |
| Repository formatting and preservation | Fresh CSharpier/whitespace/workflow checks pass; unrelated planning edits and event source remain unchanged; archive still verifies all 800 original files |

The fresh TypeScript proof uses Node 24.21.0, npm 12.0.2, TypeScript 7.0.2, .NET SDK
10.0.112, native EF 10.0.12, Npgsql EF 10.0.3 and a disposable PostgreSQL 18.6 Podman
container. NuGet restore and native formatting/test IPC needed local
sandbox escalation; no audit, analyzer or assertion was disabled. Logs are local artifacts:
`/tmp/t1-review-fixes-verification.log`; the earlier TypeScript proof remains
`/tmp/t1-typescript-verification.log`; the original T1 logs remain
`/tmp/t1-verification-final.log`, `/tmp/t1-root-build.log`, `/tmp/t1-foundation-tests.log`.
A pre-correction TypeScript-generated source tree is retained at `/tmp/foundry-t1-ts-smoke-Cedar`; the
complete proof's temporary consumers/databases are cleaned up.

Existing event/database/browser/Aspire proof reports remain historical evidence. Those
broader suites were not rerun or extended here. T1 makes no rollback, repair, production
authentication, provider compatibility or package-release claim.

## Review map and findings

Review [template configuration](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/.template.config/template.json),
[creator](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/create.ts), [creation proof](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/verify.ts),
[npm manifest](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/package.json), lockfile and TypeScript configuration
and [CI](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/.github/workflows/template.yml) for the initial-creation contract. Review
[Catalog Contracts](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog.Contracts/ICatalogQueries.cs),
[DbContext](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog/CatalogDbContext.cs),
[query](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog/CatalogQueries.cs),
[registration](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog/CatalogRegistration.cs)
and the three migration files for ownership/native persistence. Review
[composition](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/src/FoundryApplication.Host/Composition.cs),
[host](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/src/FoundryApplication.Host/Program.cs),
[adoption tests](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/tests/FoundryApplication.Adoption.Tests/CatalogJourneyTests.cs)
and [generated README](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/README.md) for lifetime, trust and
consumer obligations. No library interface or implementation changed.

- **Library:** existing ActorIdentity, Tenancy and EF ownership contracts suffice for this
  independent consumer. No extraction candidate required an interface change.
- **Template:** configurable native naming, a local source payload, deterministic creation,
  omission and refusal/atomicity behavior are newly exercised creation mechanisms. An
  independently generated composition removes the sample's compulsory event graph.
  The TypeScript/npm port preserved output byte for byte and added publication-race/partial
  engine failure checks. Subsequent review corrections reject compiler-reserved tokens,
  establish the staging SDK pin and qualify task types, with a namespace-collision and
  conflicting-parent-pin proof; no Python scripts remain in T1 tooling.
- **Sample:** a tiny editable Catalog read and native adoption fixture provide executable
  evidence. The Wholesale native composition/ownership recipes informed it; no sample
  Contracts, Access lifecycle, event migration or domain business behavior was copied.

**No new reusable runtime mechanism was proven.** T1 proves repository creation and adoption
of existing mechanisms. Authentication/tenant admission, module Contracts and native
persistence orchestration remain consumer-owned policy, not library extraction candidates
established by this slice.

Remaining gaps are intentional: business ingress/real authentication and admission,
production provisioning, package releases/upgrades, Windows/macOS creation, repository
updates, provider/preset alternatives and additional capabilities each need a separate
supported composition and review. Event repair, messaging and removal of existing event
work remain outside T1. This single composition is the stop point for owner review.
