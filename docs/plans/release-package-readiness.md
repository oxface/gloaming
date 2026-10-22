# Release package and clean adoption readiness

## Outcome

Prove the active library inventory can be packed as NuGet artifacts and consumed
outside this repository, with independently selected package segments. Existing
project-reference samples remain valuable capability proofs; they do not establish
that package contents, metadata and dependency closure survive distribution.

## Scope and acceptance

The executable [release harness](../../tools/release/README.md) packs all 23 current
`Gloaming.*` libraries under one unique local prerelease version. Verify assembly,
XML API documentation, packaged README, Apache-2.0 license expression, repository
metadata, package identity and internal dependencies. Render README links for
package browsing and reject missing repository targets.

Create new .NET consumers outside the worktree. Restore exact versions from the
local feed, with Gloaming source mapping and an isolated consumer package cache.
Reject project references and unexpected Gloaming dependency families. Run core
actor/tenancy and codec/history profiles; run a real PostgreSQL ownership/outbox/audit
profile; compile and execute a consumer selecting the full library inventory.

The PostgreSQL proof uses explicit consumer-owned mappings and save/transaction
boundaries. Prove unsaved/uncommitted invisibility, commit, rollback, native
constraint failure after an earlier save, fresh-context recovery, ownership query
filtering and rejected foreign tracked writes. PostgreSQL configuration is required;
the full command must fail rather than silently skipping native persistence.

## Boundaries

This increment changes distribution metadata and adds a repository proof harness;
it proposes no new library interface. Business documents, event aliases, tenant
keys, audit classification and outgoing routes in the tiny consumers are proof
policy, not library/template conventions. No new reusable mechanism is proven.

This does not authorize publication. Package naming/metadata and intended release
version receive owner review. Broker transport behavior, every package's full
functional suite, security review, independent architecture/code review and
production deployment evidence remain separate gates. The harness intentionally
requires third-party dependencies from nuget.org and a PostgreSQL CREATEDB role
on a disposable server. Neither the role requirement nor test schema is a
production deployment recommendation.

## Evidence

Record dated new execution and limitations in the
[release report](../reports/release-package-readiness.md). Preserve the generated
manifest/package hashes and distinguish executed results from planned coverage.
