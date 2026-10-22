# Release package and clean adoption readiness

Date: 2026-10-22. Scope: local package distribution and clean consumer adoption.

## Outcome

Added an executable TypeScript harness and small consumer-owned C# programs under
[`tools/release`](../../tools/release/README.md). All checks run against actual local
NuGet archives and clean consumers outside the repository. No new reusable library
mechanism was proven or extracted; package metadata and adoption wiring are the
deliverables. Existing capability evidence is not reclassified as package evidence.

## New verification

The full harness passed on .NET SDK 10.0.401, Node 24.21.0 and a disposable native
PostgreSQL 18.6 server. All 23 actual `.nupkg` archives passed DLL/XML/README,
license expression, author, repository, identity/version and internal dependency
graph validation. The packaged README bytes matched rendered files; missing local
link targets were rejected and nine existing targets were corrected in the owning
documentation integration.

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

The successful local version was `1.0.0-local.1792693005870.22417faa`. The evidence
directory is `/home/oxface/.cache/tmp/gloaming-package-proof-fe8hAz`; its
`manifest.json` records individual archive hashes and dependency metadata. The
manifest SHA-256 is
`60276d7eccbfa11b7fc95ce193a06490d665944fe9bb7fc38d59608aff6a8c17`.
This directory is local review evidence, not a published artifact.

The initial consumer attempt was rejected because installing the shared EF save
integration requires registering every model-selected capability. Adding explicit
`AddOutboxValidation` and `AddAuditValidation` alongside ownership validation
resolved the consumer wiring; no library change or speculative compatibility shim
was needed. The successful rerun exercised those real registrations.

The TypeScript typecheck and TypeScript/JSON/README formatting checks passed.
Consumer C# was formatted with CSharpier. These are new results, independent of
archived project-reference proofs.

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
  library package metadata and XML documentation settings, owned by the integration
  change rather than this harness.

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
