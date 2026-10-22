# S1: sample, experiment and archive consolidation

Status: owner-requested pre-v1 work, 2026-10-22. Assessment/planning now; implementation,
relocations and deletions require a concrete owner-reviewed map. No files are moved/deleted by
this plan. The [closure audit](../reports/v1-slice-closure-audit.md) records the source inventory
and important operational obligations that must survive consolidation.

## Target and timing

Maintain three distinct outputs:

1. **Independent Gloaming adoption examples beside their family**, normally under
   `src/Gloaming.<Family>/samples/`, with family-owned documentation/tests. Group examples by
   capability or real adoption obligation rather than creating another host for every slice.
   Preserve deliberately different adopters when they prove optional dependencies, domain-model
   independence, tenantless use or another materially different contract.
2. **One coherent Wholesale reference product** under `samples/Wholesale`, composed from its
   module Contracts and owned persistence. Expose the proven behavior through its normal API,
   worker and maintenance entry points instead of retaining one product executable per old slice.
   Separate API/worker/setup roles are legitimate deployment boundaries; one product does not
   mean a single process, DbContext or shared transaction.
3. **A small set of generated-template rehearsals**, demonstrating actual supported creation
   choices and omission/independent adoption. Do not reproduce every family contract suite in
   each generated application or retain experiments solely to fill a template matrix.

Inventory and scope this work before further host proliferation. Consolidate after the selected
functional behavior is known and before the final independent code/architecture reviews.
Coordinate T2 with these canonical sample sources; avoid first promoting a throwaway demo into
the template and then moving/deleting it. Archive retirement happens after functional and
selected template slices are complete and their evidence has been transferred.

## Required concrete change map

Before implementation, classify every sample/experiment project and active proof:

| Field | Required decision |
| --- | --- |
| Current owner and path | Library contract, independent adoption, Wholesale product, template rehearsal or superseded experiment. |
| Keep/merge/move/remove | Exact destination or replacement behavior; do not use vague “cleanup later.” |
| Unique obligation | What the sample/test proves and whether another active check really proves the same boundary. |
| Runtime composition | API/worker/setup/maintenance roles, DI/context ownership, public entry points, credentials and dependencies. |
| Data/evidence disposition | Migrations, persisted aliases/schemas, literal fixtures, seeded data, historical results and any compatibility repair. |
| Tooling impact | Project references, solution, CI jobs/path filters, architecture assertions, template source manifests and commands/docs. |

Use one coherent map split into reviewable relocations/merges. Do not change unrelated library
interfaces or business semantics just to make folders line up. Any justified behavior repair
is explicit in the reviewed scope, with its own meaningful verification.

## Tests and dependencies

- Family unit/provider tests verify mechanisms. Family-local examples verify independent
  adoption. Wholesale tests verify actual product integration. Template tests verify generated
  repositories. Retain each distinct obligation without copying identical implementation tests
  across all four levels.
- Preserve PostgreSQL/RabbitMQ process, concurrency, rollback, identity/deduplication, restricted
  credential/RLS, historical payload and migration proofs. A test count is not a proof map;
  identify equivalent retained assertions before removing a duplicate.
- Keep genuine domain-model isolation, alternative mapping and tenantless-adoption boundaries.
  Do not merge projects when that would invalidate their independent-adoption evidence.
- Remove family-example dependencies on Wholesale, including the current Messaging hosts'
  ServiceDefaults dependency, while retaining native host/OTel integration. Consumer-owned
  shared setup is not automatically a new Gloaming infrastructure package.
- Consolidate finite fixtures/setup and actual business paths. Remove obsolete demos, dead
  registrations, superseded wrappers and duplicate host-only plumbing when their behavior and
  proofs have an explicit retained owner. Do not conceal deleted behavior with weakened tests.
- Run appropriate moved/affected tests, architecture checks, template rehearsals and CI path
  selection checks. Record new results separately from the original slice reports.

## Archive retirement after the selected slices

The owner explicitly permits removal of `archive` after the slices are done. This future
removal supersedes the earlier freeze only for the exact reviewed retirement scope; preserve
the archive until then. Do not create another frozen copy or reconstruction patch by default.

Before removal, map every still-needed fixture, literal payload, migration comparison and
behavioral finding to current owned evidence. Retain needed material with its active owner;
remove superseded experiments rather than making active code depend on archive internals.
History/provenance can point to a verified immutable Git revision instead of broken local links.
New verification must not be replaced by archived test counts.

The deletion map must include `archive/`, the archive checksum verifier and manifest dependency,
its CI step, archive-only path-filter/dependency exclusions, active instructions/README/development
links and relevant architecture/template checks. Review the original repository-script preservation
rule and AGENTS archive-preservation instruction as part of that change. Do not blindly remove
checks that also protect active consumers. Do not delete unrelated current migrations or fixtures.

## Completion and final review

S1 is complete when the canonical examples/product/template entry points are documented and
runnable, the reviewed redundancy is removed, each retained proof has an active owner, CI
selects the relocated graph correctly, and archive retirement is complete after its prerequisites.
No independent family example depends on Wholesale. Consumers can discover setup and supported
limits beside the family; product users follow coherent Wholesale journeys rather than slice IDs.

Then run separate **independent code review** and **architecture/isolation review (P2)** against
the final cleaned tree, before release sign-off. Review proof loss, project/package boundaries,
consumer API clarity, transaction/security/lifecycle obligations and template/package adoption.
These are reviews of the changed product, not a new parallel framework initiative. Address
important v1 findings inside their owning block; defer others only with explicit owner disposition.
Release readiness closes packaging/install/restore/documentation and supported-composition checks
on that reviewed tree. No library mechanism or runtime guarantee is claimed by this plan.
