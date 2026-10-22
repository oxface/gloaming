# V1 integration execution ledger

Fixed base: `790a8b76b7a519039f61434f330d2ed1b07d70e9` (fetched origin/main).
Integration branch: `feat/v1-integration`. The owner explicitly authorizes implementation,
interface decisions, local staging/commits and branch integration for this goal. No push,
publishing, release tag or external application change is authorized by that instruction.
Existing owner review snapshots and unrelated work remain preserved.

## Completion requirements

| Block | Required evidence | Status |
| --- | --- | --- |
| WF2 deployment continuity | Old/new executable PostgreSQL adoption, retained workflow/reply continuity, incompatible deployment failure/recovery, supported procedure and extraction assessment. | Implemented and merged; seven integrated PostgreSQL tests passed. Two-axis review complete and spacing finding corrected; final full-diff review remains. |
| B2 population | Concrete producer/receiver public contract; concurrent copy/update overlap, deletes, duplicates, restart, atomic receiver progress and readiness under real infrastructure. | Implemented and merged, including reviewed malformed-wire correction and ADR. Fourteen PostgreSQL tests passed; canonical Worker/IntegrationTests adoption remains. |
| P1 projections | Actual native query evidence, explicitly justified consumer read model if needed, supported limits and mechanism-versus-policy decision. | Assessment merged, both review axes clear. Native queries suffice for same-module needs; B2 supplies the justified receiver-owned view without a generic engine. |
| S1 sample consolidation | Reviewed exact project/proof map; independent family-local examples, coherent Wholesale product and repaired CI/docs/references without lost unique tests. | Independent family relocation and canonical Wholesale integrated. Product implementation passed 696 provider/process, six browser/runtime and 101 architecture cases. Directory consolidation and archive retirement remain required. |
| T2 template adoption | Selected DDD/CQRS/worker composition in generated repositories, explicit optional dependencies, clean generated build/migration/runtime and omission proofs. | Implemented and merged. Two-axis review complete and ten spacing corrections applied. Integrated default/renamed/worker rehearsal passed all 23 cases, three zero-warning builds and creation/style/omission checks; see /tmp/v1-integrated-template.log. |
| Archive retirement | Transfer needed literals/provenance/proofs to active owners, delete frozen archive and obsolete verifier/references after functional/template completion. | Pending transfer map. |
| Per-slice reviews | Requested code-review skill: separate parallel Standards and Spec reviews against fixed commit bases, extraction/assumption assessment, all actionable findings resolved and reverified. | WF2/P1/T2/B2 reviewed with findings corrected; S1 and package readiness reviews remain. See the separate-axis review record. |
| Final code and P2 architecture review | Full integration diff against fixed main base, two-axis review then architecture skill with standalone visual report; fix findings and rerun affected checks. | Pending final tree. |
| Release readiness | Correct package graph, XML/public docs, actual local package installation/clean consumer adoption, source/template checks, relevant provider/runtime tests and full requirement audit. | All 23 ordinary SDK NuGet archives and four clean package-only profiles passed, including native PostgreSQL ownership/outbox/audit atomicity. Public XML documentation enforced; scoped CI integrated. Two-axis review completed, normal-pack README and summary findings corrected; correction inspection and final tree audit remain. |

The five explicitly approved post-v1 M2 supplied-feature deferrals remain as recorded in
the merged retention scope/report; this goal does not silently restore them. Other historical
optional items are not automatically selected by appearing in a report. Every selected
block above must finish with evidence, not an easier substitute or an unapproved deferral.

## Review questions for every block

- Are the supported assumptions and failure paths demonstrated by current executable evidence?
- What repeated technical work remains in consumers? Would a small Gloaming mechanism remove
  that work after including adoption, registration, customization and support cost?
- Which responsibilities are actual business/deployment policy rather than mechanisms?
- Are important v1 gaps resolved inside the block, or explicitly dispositioned by the owner?

Record review outcomes and commands/results in the corresponding reports. Historical test
counts are not new verification. Final integration must preserve tenant/actor establishment,
module persistence ownership, native EF transaction semantics, retained identities and the
independent event-free composition. Temporary rough edges do not satisfy completion.

Current [per-slice review record](../reports/v1-integration-reviews.md) preserves Standards
and Spec findings separately. The [package report](../reports/release-package-readiness.md)
records actual archive and external-consumer execution, rather than project-reference build
results. These partial completion records do not close the remaining integration gates.
