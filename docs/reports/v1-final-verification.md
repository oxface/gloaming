# V1 integration verification and completion audit

Date: 2026-10-22. Branch: `feat/v1-integration`.
Fixed integration base: `790a8b76b7a519039f61434f330d2ed1b07d70e9`.
Baseline before the final test/report correction: `1eb3865`, including the exact six-file
CI/spacing/package-evidence checkpoint approved by the owner. The final correction repairs
the independent browser assertion for the seven protected Sales tables and reconciles
documentation. Full runtime
verification of that correction passed all six cases.

Status: all selected implementation, execution and review requirements are verified locally;
ready for owner full-diff review. No push, pull request, package publication or release tag
is performed. This verification does not authorize those actions.

## Required outcomes and authoritative evidence

| Requirement | Implementation and proof owner | Current disposition |
| --- | --- | --- |
| WF2 retained progress across deployment | [Selected scope](../plans/wf2-deployment-continuity.md), separately compiled Earlier/Current consumers, literal protocol fixtures and seven PostgreSQL process cases. | Implemented and merged; seven cases passed in the integrated native matrix. Compatible continuation, rejection/restoration and save-before-commit interruption are exercised. No mixed-version runtime or deployment coordinator is claimed. |
| B2 initial population plus ongoing changes | [Protocol report](b2-dispatch-directory-population.md), [canonical adoption](b2-directory-product-adoption.md), Sales/Dispatch public Contracts, Directory Worker and integration tests. | Implemented and merged. The fresh complete canonical run passed 713 cases, zero skipped. Producer-defined revisions, withdrawal fences, overlap, restart, atomic progress and baseline readiness are the supported contract. Earlier fourteen-case and 713-case checkpoint results remain separately dated evidence. |
| Useful extraction and consumer burden | [Route selection contract](../../src/Gloaming.Messaging/docs/route-selection.md), provider tests and different Directory/reservation publishers. | Optional copied/validated route selection is behind the existing PostgreSQL claim/lease mechanism. Full-diff code and architecture reviews found no additional justified extraction; disclosure, admission, revision meaning, merge/readiness and native save/commit remain consumer-owned. |
| P1 native query assessment | [Assessment](p1-projection-assessment.md), actual Sales/Inventory/Purchasing query code and retained query proofs; B2 owns the independent receiver view. | Native joins/filters satisfy selected same-module reads. B2 supplies the concrete availability/population need. No generic projection engine is justified or claimed. |
| S1 coherent product and independent examples | [Exact transfer map](s1-wholesale-consolidation.md), family-local examples, canonical API/Worker/IntegrationTests and architecture assertions. | Integrated. All 69 inherited product test files and 302 Fact/Theory methods were independently reconciled; behavior/assertion ownership matters separately from counts. Fresh full product and six-case runtime execution passed. |
| Archive retirement with retained evidence | [Retirement record](s1-archive-retirement.md), immutable Git objects, current owned fixtures/migrations. | Exactly 802 tracked archive files plus the obsolete verifier removed. Needed literals/proofs transferred; original source and manifest remain addressable by immutable revisions. No replacement archive or reconstruction patch. |
| T2 generated composition and omission | [Template report](t2-template-patterns.md), native creator and external PostgreSQL consumers. | Integrated rehearsal passed all 23 cases in default, renamed-namespace and optional-worker applications; three zero-warning builds, native style/analyzer and creation/omission checks. Default remains event/messaging-free. |
| Distribution and clean adoption | [Package report](release-package-readiness.md), ordinary native SDK preparation and independent artifact/consumer harness. | Final integrated run passed all 23 actual archives and four package-only consumers, including PostgreSQL ownership/outbox/audit commit/rollback/fault recovery. No ProjectReference or unexpected Gloaming family substitutes for package adoption. |
| Per-slice and final code review | [Separate-axis record](v1-integration-reviews.md). | Findings corrected; final independent Standards and Spec inspections each found zero new actionable issues. Pending CI/spacers were inspected separately from committed source. Inspection is not fresh test execution. |
| Final P2 architecture/isolation review | [Durable review decision](v1-integration-reviews.md#p2-architecture-and-isolation-review) and standalone visual artifact. | Completed after both code-review axes. No required architecture fix or further justified mechanism. Speculative navigation changes were rejected explicitly, not left as required v1 gaps. |
| Relevant native/runtime and repository checks | Exact executions below. | All 31 native test projects passed 1,804 cases; all six browser/runtime cases passed. Required source/tooling checks passed. |
| Preserve unrelated work and stop before push | Primary index and original orchestration worktree inspection; topic ancestry/upstream and remote refs. | Primary index remains empty. Original orchestration's six pending documents remain untouched. All implementation branches are integrated into this topic branch; no upstream points it at main. No push. |

## New final-tree execution

All commands used .NET SDK 10.0.401 and Node 24.21.0 where applicable. Native container
proofs used the existing rootless Podman endpoint and real PostgreSQL/RabbitMQ. Local runs
bound their own process trees where needed with `DOTNET_PROCESSOR_COUNT=2`,
`MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`; test assertions,
explicit competing operations and native transactions were not reduced or replaced.

| Check | New result / retained log |
| --- | --- |
| Full solution restore | Passed; `/tmp/v1-final-restore.log`. |
| Full solution build after the final two separators | Passed, zero warnings/errors; `/tmp/v1-final-build-current.log`. |
| CSharpier including generated source | Passed, 963 files; `/tmp/v1-final-csharpier.log`. |
| Native whole-solution style/analyzers, verify-only/no restore | Both passed; `/tmp/v1-final-style.log`, `/tmp/v1-final-analyzers.log`. |
| Integrated external template rehearsal | 23 cases, three zero-warning builds and strict creation/omission proofs; `/tmp/v1-integrated-template.log`. These generated sources were unchanged afterward. |
| Context console through relocated project | Passed; `/tmp/v1-final-context-console.log`. |
| Integration commit messages | All commits from fixed base through 10d938f passed; `/tmp/v1-final-commitlint.log`. The later 1eb3865 checkpoint also passed its normal commit-message hook. |
| Template TypeScript type/format checks | Passed; `/tmp/v1-final-template-check.log`. |
| Ordinary packages and four clean consumers | Passed; manifest `/home/oxface/.cache/tmp/gloaming-package-proof-oKIDjX/manifest.json`, SHA-256 `0c33ef04852773860e031af1b7a288d531d0b67992c55e21cdf59bb33d54170c`. |
| CI dependency selection | 1,921 actual job/input pairs and 97 positive/negative probes passed; [configuration evidence](s1-ci-selection-repair.md). Hosted execution remains unclaimed. |
| Local Markdown targets | 3,017 links across 332 tracked/new documents resolved, including final audit additions; `/tmp/v1-final-links.json`. |
| Native solution test projects | All 31 projects passed 1,804 cases, zero failures/skips; `/tmp/v1-native-recovery-3KdqZU/results.json`. Exact solution project set and every successful log/count were independently rechecked. |
| Final Aspire/Keycloak/browser runtime | Passed all six cases, zero failures/skips, 4m31s; `/tmp/v1-final-runtime-corrected.log`. The first failed attempt and correction are recorded separately below. |
| Runtime assertion correction build/style/analyzers | Build passed with zero warnings/errors; native style/analyzers and CSharpier passed. `/tmp/v1-runtime-rls-build.log`, `/tmp/v1-runtime-rls-style.log`, `/tmp/v1-runtime-rls-analyzers.log`. |

The first broad native attempt ran three test projects concurrently. It is explicitly a
**failed attempt**, not evidence of a green matrix: product execution aborted with literal
out-of-memory output after 198 successful cases; worker children reported thread/socket
allocation failures and PostgreSQL/container errors; PersistenceDemo had five fixture-start
failures from a rootless TCP-port bind collision. OutboxDemo produced no summary before its
900-second deadline. A returned process code of zero without completed tests was rejected.
Original logs/manifest remain in
`/home/oxface/.cache/tmp/v1-native-test-matrix-dB3ZQy`.

Only exact abandoned processes/container IDs traced to this failed run were cleaned up;
unrelated running resources were preserved. Sequential bounded recovery reran all four
affected projects, was configured to stop on any repeated failure and retained each result. No assertion,
fixture contract or library source was changed merely to make this attempt green. Recovery
passed Wholesale 713, MessagingWorkerDemo 28, OutboxDemo 25 and PersistenceDemo 36 cases,
all with zero failures/skips and completed summaries. The original 27 successful projects
contribute 1,002 cases; failed/partial attempts are excluded from the 1,804 total. The original
Outbox stall's underlying cause remains unproven; it did not recur in the complete fresh run.
The combined evidence records which attempt establishes each project result.
The package report separately records its failed concurrent extraction attempt and complete
fresh successful rerun.

The first final Aspire/browser run completed six cases with five passed and one failed,
zero skipped (`/tmp/v1-final-runtime.log`). Its restricted Sales startup assertion still
expected six protected tables; B2 intentionally added the seventh, `outbox_messages`, to
the finite protected runtime catalog. This is an integration-test expectation defect, not
the resource-pressure failure above or a removed security requirement. The correction
queries the exact sorted role/table names and compares them with independent literals,
retaining all restricted browser/admission/mutation assertions. Production setup, grants,
RLS policies and startup validation are unchanged. The complete fresh six-case rerun passed,
zero skipped, in `/tmp/v1-final-runtime-corrected.log`. The exact-name check confirms the
stale expectation, rather than missing roles or missing table protection, caused the first
failure. Both follow-up review axes accepted the correction with no remaining finding.

The final audit independently matched all 31 native project paths to the current solution,
checked each selected log's complete pass/count, recomputed all 23 archive hashes and the
package manifest hash, and inspected each clean consumer's exact-version references and
resolved package closure with no project dependencies. Evidence:
`/tmp/v1-evidence-audit.json`. Core/events/PostgreSQL/all profiles resolve exactly 2/2/8/23
Gloaming packages respectively. These checks supplement actual execution; archive counts
alone do not prove runtime behavior.

## Supported limits and gap disposition

The five owner-approved post-v1 M2 supplied-feature deferrals remain in the merged
[retention scope](../plans/m2-inbox-retention.md) and family contract. No important selected
gap is silently reclassified as a new slice. Historical source-audit gaps are reconciled with
later broker recovery/shutdown, queued compatibility, retention and regeneration work rather
than reopened from stale reports.

Directory readiness is baseline traversal with eventual updates, not a global caught-up
barrier. Publication may duplicate; retained inbox identity and revision merge have explicit
lifetimes. Workflow rollout stops old workers before compatible replacement; concurrent
mixed-release orchestration is unsupported. Consumer policy owns admission, credentials,
disclosure, scheduling and business compensation. No universal authorization engine,
projection daemon, repository updater or transport framework is implied by this integration.

Package proof establishes local artifact integrity/adoption, not public-feed publication or
production capacity. Local CI selection checks establish the configured dependency map,
not a GitHub Actions run. Owner full-diff review and any subsequent push/PR remain separate.
