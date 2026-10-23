# Evidence and proof-ownership consolidation

Date: 2026-10-23. Base: `bd2adb7dea58de304aa2eb3df3c587c6fe348320`.
Status: uncommitted review batch on `docs/gloaming-library-audit`. The approved guidance slice
was checkpointed as `bd2adb7`; its complete reviewed snapshot and checkpoint checks matched.
All new work remains unstaged; no push or publication is performed.

## Outcome and dispositions

Retired **129 dated reports and four ADRs / 20,223 original lines** through verified Git
provenance. The [active audit](gloaming-library-audit.md) now keeps current direction,
checkpoint/evidence links and one remaining queue rather than its full implementation diary.
The [provenance record](s1-archive-retirement.md) identifies the exact baseline and current
proof owners. Original research source pins, rejected alternatives and dated executions remain
available in Git; no frozen archive or reconstruction patch is added.

Every former report is execution/research evidence rather than a current consumer contract.
The disposition by subject is:

| Former material | Current owner/disposition |
| --- | --- |
| Library extraction, provider, recovery, retention, telemetry and deployment executions | Current family contracts/tests and the proof map; original commands/results remain pinned dated evidence. No supported protocol is removed. |
| Access/authorization/RLS research | [Authorization/isolation brief](../research/authorization-and-isolation.md), native sample permission/isolation guides and family contracts. No engine selection. |
| Domain/event/EF/external-framework comparisons | [Domain/module](../research/domain-and-module-composition.md) and [native integration](../research/native-persistence-and-delivery.md) briefs retain useful alternatives and source pins. Earlier proposed interfaces are not current tasks. |
| B1/B2/bootstrap/projection research | [State-transfer brief](../research/integration-state-transfer.md) retains sparse/full-state alternatives and proof obligations. Rejected feed/retired directory policy is not a supported library feature. |
| Template, naming, relocation, CI/dependency repairs and earlier release reviews | Current guides/configuration plus immutable original records. Retired tooling guarantees stay retired; earlier sign-off does not certify this cleaned tree. |
| Superseded 0007/0013, rejected 0015, retired 0022 | Git history, accepted successor decisions and research rationale. No ADR numbers reused. |

The [ADR index](../adr/README.md) routes to 18 retained decisions. Reconciled stale pending-review
status and descriptions of removed prepared append handles, the pre-read gate, separate workflow
core, thin audit/OTel adapters, and old inbox failure/retry state. These reflect later implemented
owner decisions, not new architecture or authorization. Family wording now describes native audit
save composition/JSONB mapping, supported outbox backlog observation, current optional RLS and
unsupported state-transfer scope. The extraction gate and guarantees remain unchanged.

External research was distilled from the existing dated source inspection; no external documents
were fetched again or external implementation tests executed for this consolidation. Version-specific
claims remain with their original pins. Current family contracts take precedence over old proposals.

## Executable test cleanup and retained obligations

Removed five architecture cases that froze exact provider test-project package lists:
RabbitMqTests, Persistence PostgresTests, TenancyPostgresTests, AuditPostgresTests and
EventSourcingPostgresTests. Their xUnit/Testcontainers/native package inventories are editable
test setup. Four actual library dependency checks remain with the same expected declarations;
compiled library/module isolation and all executable provider/adoption tests remain.
The architecture suite intentionally changes from **89 to 84 cases**.

Removed their five copied csproj fixtures and two duplicate Worker ProjectReferences from the
architecture project. No runtime/test project is retired. This gives up policing unused
references in those five test projects; it preserves library declaration policy and executable
consumer coverage. Native XML checks do not evaluate every MSBuild import/condition, so no
complete transitive dependency-analysis guarantee is inferred.

The [proof map](../../tests/README.md) covers all **30 native test projects**, their owner and
unit/provider/adoption/process boundary. Static inventory finds every project in the solution
and a native CI command. This is selection-input review, not proof of GitHub scheduling or that
every runtime case passed freshly. Tests protect Gloaming, focused consumers or retained Wholesale;
compatibility literals, process-death fixtures and compiled Earlier/Current deployments retain
separate obligations. No further deletion is justified solely by overlapping API names.

Existing CI lanes/hooks remain: family/provider/adoption, Wholesale integration, runtime/browser,
package-only adoption and lightweight tooling. No retired application lane or custom dependency
selector is restored. This completes the selected relevance/consolidation pass; final independent
review may still find defects, and the owner-selected extra documentation/test pass remains open.

## Fresh verification

- Complete architecture suite: **84 passed**, no skips/failures. Native build/test used the changed
  project declarations and retained all library/module/schema rules.
- CSharpier checked **858 files**; native solution style and analyzers passed. Repository and
  release-tool TypeScript/format checks passed; all **six** checker regression cases passed.
- **796** local Markdown targets and **294** literal project/source targets resolve; four
  MSBuild expressions/globs are explicitly skipped. The final report adds no new file links.
- All **133** retired paths match their recorded Git blobs. **155** retained baseline references
  resolve to **82** unique blobs; supplied heading fragments and the six links into rewritten
  local headings were checked separately. Remote reachability is not certified.
- All **21 archives and six isolated package-only consumers** passed through the real pack
  command, including native README rendering and PostgreSQL ownership/outbox/audit/continuation
  execution. No ProjectReferences in adopters; the disposable database was stopped/auto-removed.
- **866 of 868** owned source/project/JSON/SQL paths are byte-identical; the only changes are
  the architecture test and its csproj. No owned source/data path is added or removed.
  Library/sample implementations, provider tests, current fixtures and migrations remain intact.
- All **30** native test projects remain in the solution and explicit native CI commands.
  Root `docs` contains **33 Markdown files**, down from **159** at this batch's base, including
  three reports, four research briefs plus their index, and 18 current decisions plus their index.
- Whitespace checks pass and the existing staged snapshot remains unchanged.

Package proof version: `1.0.0-local.1792748783656.g485aa825`.
Evidence: `/home/oxface/.cache/tmp/gloaming-package-proof-Z6Vah6/manifest.json`;
SHA-256 `3ae482688b9e6687567828d4033ab955cd8e64478ce0c88c16d82a244ee9548b`.

Logs use `/tmp/gloaming-evidence-{architecture,format,style,analyzers,tooling,packages}.log`.
Local retirement inventory: `/tmp/gloaming-evidence-retired.json`; source baseline:
`/tmp/gloaming-evidence-owned.json`; test-project inventory:
`/tmp/gloaming-evidence-test-inventory.json`. These are local evidence, not shipped proof harnesses.

Other native family/provider, Wholesale and browser suites were not rerun for unchanged runtime
source in this batch. Their prior results remain historical; fresh complete integration remains
required against the final cleaned tree. Package-only PostgreSQL execution is a separate fresh
check and does not replace those suites.

## Review and remaining work

Review the [architecture test](../../tests/ArchitectureTests/AdoptionDependencyTests.cs) and
[project](../../tests/ArchitectureTests/ArchitectureTests.csproj), proof map, current ADRs,
four research briefs, family contract corrections, compact audit/provenance and the exact
retirement set. Most remaining Markdown diffs only replace retired report/ADR links with pinned
Git targets. No existing index snapshot is changed.

**No new reusable Gloaming mechanism was proven or proposed.** This removes stale guidance and
low-value test setup snapshots while retaining technical contracts. Native setup/host composition
is consumer-owned; Wholesale authorization, domain/payload meaning, migrations and future Umbrine
workflow policy remain consumer-owned. No new template, library API, compatibility promise or
feature deferral is selected.

Remaining required work: integrated verification and separate independent code/architecture reviews,
fixes and relevant rechecks, then the owner's extra documentation/test pass. Expect 1–2 review/
verification increments plus at least one extra pass, with no fixed cap. The later clean-context
sweep is a separate owner session. Local proofs do not authorize push/publication or establish
production capacity or broader provider support.
