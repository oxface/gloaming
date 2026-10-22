# Release package and clean adoption readiness

Date: 2026-10-22. Scope: local package distribution and clean consumer adoption.

## Outcome

Added an executable TypeScript harness and small consumer-owned C# programs under
[`tools/release`](../../tools/release/README.md). All checks run against actual local
NuGet archives and clean consumers outside the repository. No new reusable library
mechanism was proven or extracted; package metadata and adoption wiring are the
deliverables. Existing capability evidence is not reclassified as package evidence.

## Regular SDK packaging correction

Independent standards review identified that the first successful harness run
supplied its own rendered README through `PackageReadmeSource`, while ordinary
`dotnet pack` shipped raw relative links. That run established the custom-input
artifact, leaving ordinary package browsing unproven. This discrepancy is fixed
in [`src/Directory.Build.targets`](../../src/Directory.Build.targets): a native SDK
Roslyn task renders the owning README before NuGet's `_GetPackageFiles` target
collects `Pack=true` items. It writes under `obj`, verifies local targets and
repository containment, and URL-encodes path segments. It runs for regular packing
and packing without a build, requires no Node/npm, preserves already-absolute
external-source links and is not included in distributed packages.

The harness now removes its README input override and compares the regular SDK
artifact with independently rendered TypeScript expectations. Native task probes
also verify encoded Unicode/space/apostrophe/parenthesis paths, both separator
spellings, directory/anchor/external links and rejection of missing or escaping
targets. A failed render must leave the previous output unchanged. The complete
ordinary-pack proof passed after this correction, covering all 23 archives and
all four clean-consumer profiles below. Additional targeted packs without a build
passed for the normal owning README and an external already-rendered source;
the extracted README bytes matched independent expectations in both cases.

## New verification

The corrected full harness passed on .NET SDK 10.0.401, Node 24.21.0 and a disposable native
PostgreSQL 18.6 server. All 23 actual `.nupkg` archives passed DLL/XML/README,
license expression, author, repository, identity/version and internal dependency
graph validation. Ordinary `dotnet pack` received no README override. Packaged
README bytes matched independent TypeScript expectations. Missing and escaping
link targets were rejected by the native task, and no repository task assets were
included under package `build`, `buildTransitive` or `tools` directories. Nine
existing targets were corrected in the owning documentation integration before
the first proof.

| Clean package consumer | Resolved Gloaming packages | New execution |
| --- | ---: | --- |
| Actor identity + tenancy | 2 | Lifecycle, opaque keys, actor-kind distinction, concurrent reads and operation independence passed |
| Events serialization + history | 2 | Retained literal JSON, stable event alias, serializer options, unknown identity/schema errors, contiguous history and regression rejection passed |
| PostgreSQL ownership + outbox + audit | 8 | Unsaved/uncommitted invisibility, native commit, explicit rollback, failed later save after earlier SQL, fresh-context recovery, query filtering and foreign-write rejection passed |
| All packages | 23 | Restore, compile and executable startup passed |

Every consumer used exact package versions, a temporary local Gloaming feed and
an isolated package cache. Restored assets contained zero project references and
exactly the expected Gloaming dependency closure. The staged review snapshot was
unchanged across the command. The temporary PostgreSQL database and server were
removed. Missing PostgreSQL configuration was separately executed and rejected
before packing, confirming that the full command cannot silently skip it.

The successful ordinary-pack local version was `1.0.0-local.1792693817990.14b7219b`. The evidence
directory is `/home/oxface/.cache/tmp/gloaming-package-proof-GQAzZf`; its
`manifest.json` records individual archive hashes and dependency metadata. The
manifest SHA-256 is
`8a7a0887609b5967efbdf1f23a9a3d20524353c143f01c228ce8c7196b78707b`.
This directory is local review evidence, not a published artifact.

The initial consumer attempt was rejected because installing the shared EF save
integration requires registering every model-selected capability. Adding explicit
`AddOutboxValidation` and `AddAuditValidation` alongside ownership validation
resolved the consumer wiring; no library change or speculative compatibility shim
was needed. The successful rerun exercised those real registrations.

The TypeScript typecheck and TypeScript/JSON/README formatting checks passed.
Consumer C# and the new SDK props/targets were formatted with CSharpier; the
props/targets format check passed. These are new results, independent of archived
project-reference proofs and the earlier custom-README-input run.

## Review-worthy files

- [`verify.ts`](../../tools/release/verify.ts): package inventory, exact versions,
  archive metadata/content validation, README rendering, isolated restores and
  dependency-family assertions; command logging and index preservation.
- [`Core.cs`](../../tools/release/consumers/Core.cs) and
  [`Events.cs`](../../tools/release/consumers/Events.cs): small executable package
  consumers with independent expected outcomes.
- [`Postgres.cs`](../../tools/release/consumers/Postgres.cs): explicit native
  database creation/cleanup, mappings, model-selected capability registration and
  transaction/fault recovery.
- [`src/Directory.Build.props`](../../src/Directory.Build.props): the associated
  library package metadata, XML documentation settings and owning README selection.
- [`src/Directory.Build.targets`](../../src/Directory.Build.targets): repository-only
  native README preparation for ordinary SDK packing, including no-build packing.

## Findings and remaining gates

Consumer-owned policy includes trusted actor establishment, tenant keys, audit
classification, event aliases, JSON options, table mappings, selected outgoing
routes and save/commit ownership. The harness adds no template behavior and no
sample business rule. Existing sample usage is historical/background evidence for
this packaging increment; only the new clean package consumers establish the
distribution claims listed here.

No packages are published. Real broker delivery, RLS against bypass SQL, production
credentials/least privilege, every functional capability, independent final code
and architecture review and owner release approval are not established by this
proof. All package selection, shipped metadata and documentation remain reviewable
before release. There is no silent PostgreSQL skip path.
