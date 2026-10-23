# Repository guidance and reference consolidation

Date: 2026-10-23. Base: `0d43d3cf746bc5b70dec60b8864459cf97c7d65d`.
Status: uncommitted review batch on `docs/gloaming-library-audit`. The approved Dispatch
retirement is checkpointed as `0d43d3c`, with all checkpoint hooks passing. This batch is the
first repository-consolidation increment; it does not complete the remaining report/test review.

## Outcome

Retired **80 dated plans / 14,149 lines** through Git history, including competing capability
roadmaps and the old integration ledger's task-specific blanket authorization. Their historical
references now use immutable, locally verified blobs. The extraction document becomes a short
[proposal gate](../plans/library-extraction.md), and [design](../design.md) becomes current
cross-family ownership guidance linked to authoritative family contracts. The
[audit queue](gloaming-library-audit.md#remaining-cleanup-slices) is the sole current work queue.
AGENTS/workflow/entry points follow that route; exact checkpoint approval remains required.

The [provenance record](s1-archive-retirement.md) retains original archive/fixture origins and
current proof ownership without presenting old integration gaps as current instructions.
Historical reports retain dated results; their relevance and ADR status still need the next
pass. No replacement archive or frozen source copy is introduced.

The new TypeScript [reference checker](../../tools/repository/references.ts) turns prior
ad-hoc existence checks into a lightweight CI/hook obligation. It checks working-tree Markdown
file/directory targets and literal native solution/project/import/linked-fixture targets against
tracked or non-ignored source. Deleted indexed files, ignored build output and empty directories
cannot mask broken source references. It never stages files. Tests use disposable Git fixtures.
The [tooling guide](../../tools/repository/README.md#local-references) specifies supported syntax
and omissions: no heading-anchor/remote-URL validation, full Markdown rendering or MSBuild
expression/glob evaluation. Restore/build and package adoption remain separate proof gates.

Repository integrity runs the tooling checks unconditionally, including documentation-only
changes. The selected pre-commit job uses the same command. Existing family/provider/sample
jobs and explicit CI selection remain; reference checking is not a dependency-selection engine.
New pinned development dependencies match the release-tool TypeScript/Node types/formatter.
No runtime dependency is added to Gloaming.

Reconciled two stale family statements: inbox-retention's five supplied-feature deferrals
were explicitly approved at `6510f9e`/`2e1ffac`; new-service repopulation remains unsupported,
with historical proposals preserved rather than presented as an active delivery commitment.
No additional deferral or capability is selected. Removed the empty local Sales `ProfileFeed`
directory; Git does not publish empty folders.

## Fresh verification

| Check | Result |
| --- | --- |
| Repository-tool clean install | Locked `npm ci --ignore-scripts` passed; typechecking and tooling formatting passed. |
| Checker regression cases | Six passed: ordinary/encoded/image/reference links, fenced/inline examples and diagnostics, portability/ignored output, literal XML/linked fixtures, indexed deletion plus untracked docs, and CLI repository discovery/exit status. The XML-entity case caught a checker bug in the first run; decoding before semicolon-list splitting fixed it, and the complete suite passed. |
| Local references | 1,929 local Markdown targets and 301 literal project/source targets checked; zero broken references. Four MSBuild expressions/globs explicitly skipped. |
| Historical provenance | All 80 retired plan blobs plus the original extraction guide exist at the pinned base. All retained new-base historical references resolve against native Git objects; anchors/remote reachability are outside this check. |
| Preservation | All 868 owned C# sources, projects/solutions, JSON and SQL files under src/samples/tests remain byte-identical; none added or removed. Existing fixtures and migrations remain intact. |
| CI/hook wiring | Changed YAML parses; the Repository integrity step is unconditional. The selected hook caught the audit link to this report before the report existed; after adding its target, the final run passed using a nested Markdown path with installation/staging disabled. This is local wiring evidence, not a GitHub scheduling run. |
| Package adoption | All 21 archives and six fresh-cache package-only profiles passed through the real pack command, including native README rendering checks and PostgreSQL ownership/outbox/audit/continuation execution. No ProjectReferences in adopters. |
| Review state | Whitespace checks pass; staged snapshot remains unchanged. No new commit, push or publication for this batch. |

Package proof version: `1.0.0-local.1792747400856.gdca0405f`.
Evidence: `/home/oxface/.cache/tmp/gloaming-package-proof-kEOCs9/manifest.json`;
SHA-256 `b045311be135e02ddb3f7f50f8e7fbcacd3361a53f5e31b9f861faece879d7a7`.
Logs: `/tmp/gloaming-guidance-packages.log` and `/tmp/gloaming-guidance-hook.log`.
The disposable PostgreSQL container was stopped
and auto-removed. Source preservation/provenance baselines live under
`/tmp/gloaming-consolidation-{owned,retired-plans}.json` for this local review.

Existing .NET family/Wholesale/browser suites were not rerun for source-unchanged guidance
and repository tooling. Their checkpoint results remain historical evidence; fresh integrated
verification against the final cleaned tree remains required.

## Review and remaining gaps

Review [design](../design.md), [the extraction gate](../plans/library-extraction.md),
[workflow](../conventions/repository.md), the checker and its
[regression cases](../../tools/repository/references.test.ts),
[CI](../../.github/workflows/ci.yml), [hooks](../../lefthook.yml), family wording corrections
and [provenance](s1-archive-retirement.md). Most other Markdown diffs only pin historical plan
references. The deletion set is the 80 dated plan files; the current gate remains.

**No new reusable Gloaming mechanism was proven or proposed.** The executable addition is
repository integrity tooling. Existing library guarantees remain with their family contracts;
transaction/host setup is explicit consumer composition. Wholesale business policy and future
Umbrine requirements remain consumer-owned; the retired generator stays retired.

Required work still includes the detailed report/ADR relevance pass, test and CI relevance,
final integrated package/sample/runtime verification, and independent code/architecture reviews
with fixes. Then perform the owner-requested additional documentation/test pass. The owner's
later separate-session sweep is not evidence supplied here. The planning range after this
batch is 2–3 consolidation/review increments plus at least one extra pass, with no fixed cap.
