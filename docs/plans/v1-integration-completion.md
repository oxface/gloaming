# V1 integration execution ledger

Fixed base: `790a8b76b7a519039f61434f330d2ed1b07d70e9` (fetched origin/main).
Integration branch: `feat/v1-integration`. The owner explicitly authorizes implementation,
interface decisions, local staging/commits and branch integration for this goal. No push,
publishing, release tag or external application change is authorized by that instruction.
Existing owner review snapshots and unrelated work remain preserved.

## Completion requirements

| Block | Required evidence | Status |
| --- | --- | --- |
| WF2 deployment continuity | Old/new executable PostgreSQL adoption, retained workflow/reply continuity, incompatible deployment failure/recovery, supported procedure and extraction assessment. | Complete: implemented and merged; seven integrated PostgreSQL tests passed. Per-slice/final two-axis review complete and spacing finding corrected. |
| B2 population | Concrete producer/receiver public contract; concurrent copy/update overlap, deletes, duplicates, restart, atomic receiver progress and readiness under real infrastructure. | Complete: implemented and merged, including malformed-wire correction, ADR, canonical API/Worker adoption and the demonstrated RouteKeys extraction. Fresh complete product verification passed all 713 cases; final browser verification passed all six after correcting its stale pre-B2 protected-table assertion. |
| P1 projections | Actual native query evidence, explicitly justified consumer read model if needed, supported limits and mechanism-versus-policy decision. | Assessment merged, both review axes clear. Native queries suffice for same-module needs; B2 supplies the justified receiver-owned view without a generic engine. |
| S1 sample consolidation | Reviewed exact project/proof map; independent family-local examples, coherent Wholesale product and repaired CI/docs/references without lost unique tests. | Complete: family relocation, canonical Wholesale and Directory adoption are integrated. Final 31-project native verification passed 1,804 cases; all six final browser/runtime cases passed. CI selection/spacing corrections are committed as 1eb3865 with normal hooks. The stale browser RLS assertion was corrected to independent exact role/table names, reviewed on both axes and verified by the complete runtime rerun. |
| T2 template adoption | Selected DDD/CQRS/worker composition in generated repositories, explicit optional dependencies, clean generated build/migration/runtime and omission proofs. | Implemented and merged. Two-axis review complete and ten spacing corrections applied. Integrated default/renamed/worker rehearsal passed all 23 cases, three zero-warning builds and creation/style/omission checks; see /tmp/v1-integrated-template.log. |
| Archive retirement | Transfer needed literals/provenance/proofs to active owners, delete frozen archive and obsolete verifier/references after functional/template completion. | Implemented at 836e4ed: exactly 802 archive files and the obsolete verifier removed; current fixtures/migrations preserved and historical links verified against immutable Git objects. The retirement report records the transfer and byte-level checks. |
| Per-slice reviews | Requested code-review skill: separate parallel Standards and Spec reviews against fixed commit bases, extraction/assumption assessment, all actionable findings resolved and reverified. | Complete: WF2/P1/T2/B2/package readiness/Directory/S1 reviewed with corrections. S1 CI selection and spacing fixes passed the dependency-map audit and final review. The later runtime assertion and report-description corrections also passed both follow-up axes. No actionable finding remains. See the separate-axis review record. |
| Final code and P2 architecture review | Full integration diff against fixed main base, two-axis review then architecture skill with standalone visual report; fix findings and rerun affected checks. | Complete: independent Standards/Spec inspections found no remaining actionable issues across the full integration and later test/documentation correction. P2 reports no required changes or additional justified extraction; its visual artifact and rejected speculative changes are recorded in the review report. Final execution and requirement audit passed. |
| Release readiness | Correct package graph, XML/public docs, actual local package installation/clean consumer adoption, source/template checks, relevant provider/runtime tests and full requirement audit. | Complete for local review: all 23 ordinary SDK archives and four clean package-only profiles passed at integrated 10d938f, including native PostgreSQL ownership/outbox/audit atomicity. Public XML enforced; final native/runtime, template, source/tooling checks and requirement audit passed. No publication/push or production-capacity claim. |

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
results. The [final verification and requirement audit](../reports/v1-final-verification.md)
records the completed execution gates, failed attempts, supported limits and preserved work.
The implementation is ready for owner full-diff review; no push is authorized or performed.
