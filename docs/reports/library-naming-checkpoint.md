# Initial library naming checkpoint

Date: 2026-10-08. Base checkpoint: `b597717`.
The owner explicitly authorized the initial library naming change and its checkpoint.

Library/type/path spellings below use the current Gloaming name for navigation. The results
remain historical evidence from 2026-10-08, not executions of the current rename. Original
spellings remain in Git history. See [the current rename report](gloaming-library-renaming.md)
for the 2026-10-14 change and its verification.

## Outcome and concrete scope

All nine independently adoptable library projects now use `Gloaming.*` namespaces, project
filenames, assembly names and default package IDs. Their five source/documentation/test
directories use the same prefix. Selection and dependency boundaries remain unchanged.

| Former project prefix/name | Current project |
| --- | --- |
| ModulithFoundry.ActorIdentity | Gloaming.ActorIdentity |
| ModulithFoundry.ActorIdentity.AspNetCore | Gloaming.ActorIdentity.AspNetCore |
| ModulithFoundry.Tenancy | Gloaming.Tenancy |
| ModulithFoundry.Tenancy.AspNetCore | Gloaming.Tenancy.AspNetCore |
| ModulithFoundry.Persistence.EntityFrameworkCore | Gloaming.Persistence.EntityFrameworkCore |
| ModulithFoundry.Events.Serialization | Gloaming.Events.Serialization |
| ModulithFoundry.Events.History | Gloaming.Events.History |
| ModulithFoundry.EventSourcing | Gloaming.EventSourcing |
| ModulithFoundry.EventSourcing.EntityFrameworkCore | Gloaming.EventSourcing.EntityFrameworkCore |

Review-worthy changes are the five `src/Gloaming.*` directories, active solution/project
references, sample/template imports, architecture assembly/dependency assertions, CI and
hook test paths, template source-map/omission checks and current documentation/navigation.
Library test namespaces also use Gloaming; their test project names remain unchanged.

The repository remains Modulith Foundry. Sample, architecture and shared test-support
namespaces, the solution filename, template identity/command and generated application
naming options remain unchanged. T1 snapshots the same three selected libraries under their
new names. Its omission check recognizes both historical and current Events prefixes.

Durable event aliases/schemas, business Contracts, database identifiers, migrations, fixtures
and frozen archive are preserved. The private `ModulithFoundry:` EF annotation keys are
intentionally retained because they appear in native migration snapshots. This rename does
not require new migrations or rewrite persisted events. Obsolete ignored build caches were
preserved outside the active source layout; restored projects have new local build outputs.

Root reports retain dated outcomes and checkpoint references. Their library/type spellings,
active source links and command paths now point to the current naming and layout.

## Fresh verification

Restore and full solution build passed for all 43 active projects with zero warnings/errors.
Style, analyzers, CSharpier and template TypeScript type/format checks passed.

| Existing suite | Passed |
| --- | ---: |
| ActorIdentity / ASP.NET Core | 19 / 15 |
| Tenancy / ASP.NET Core | 17 / 39 |
| EF ownership model / PostgreSQL | 23 / 6 |
| Event serialization / history | 49 / 16 |
| Aggregate core / PostgreSQL event sourcing | 11 / 67 |
| Architecture and independent adoption | 68 |
| Context / native persistence consumers | 15 / 36 |
| HTTP identity and protected business consumer | 97 |
| Codec / raw event-storage consumers | 24 / 35 |
| Native event-persistence consumer | 165 |
| T1 external generated consumers | 5 / 5 |

All 702 active tests and 10 generated-consumer tests passed, with no skipped tests.

The PostgreSQL suites and both independently generated T1 consumers ran against real
PostgreSQL 18.6 using rootless Podman. Existing tests cover ownership, model consistency,
state-dependent writes, concurrency, rollback, rebuilding and fresh-context recovery.
T1 also rechecked deterministic snapshots, configurable names, local references and absence
of event/messaging libraries. These are fresh executions of existing proofs under the new
identities, rather than new feature tests or historical results copied from earlier reports.

Structural comparisons against `b597717` checked library/test C# and project bodies after
identity substitutions, import ordering and formatter whitespace across 117 source/project
files. All 54 active migration/fixture files remain byte-identical. Native Inventory and
Purchasing migration checks report no pending model changes. All 1,176 existing active local
Markdown links resolve, solution membership
is unchanged and the frozen archive verifier matches all 800 original files.

## Mechanisms, policy and limitations

**No new reusable mechanism was proven.** Libraries retain their existing technical contracts;
sample admission, business decisions/evolution and native save/commit remain consumer policy.
The template still provides consumer-owned setup and event-free composition.

This is a source and assembly identity change: existing external consumers must update
namespaces and references and rebuild. No compatibility facade, package publication,
GitHub organization or independent release pipeline was added. Provider/runtime support and
documented deferred capabilities are unchanged. Aspire browser/runtime and archived suites
were not independently rerun for this rename; active host projects compile, and commit hooks
retain their existing archived checks.
