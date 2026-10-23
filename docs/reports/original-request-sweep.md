# Original-request sweep

Date: 2026-10-23. Base: owner-approved checkpoint `c5acb8e`.
Status: local checks passed; uncommitted review batch. The checkpoint matched the approved
35-path snapshot and passed all normal hooks. Its worktree/index were clean before this pass.
The owner requested another sweep against the original request before their own later review.
This pass uses the existing conversation context; it is not a new independent context/reviewer.

## Assessment against the original request

| Original area | Current result and remaining boundary |
| --- | --- |
| Replace configurable template work with patterns/tasks | The generator is retired. The [patterns guide](../application-patterns.md) now includes first-behavior inputs and an actionable add-module recipe linked to real composition. A dedicated migrator is a consuming-application choice; the current sample uses finite setup branches. A routing skill/CLI is not required before repeated Umbrine work identifies a useful task. |
| Make Gloaming primary; retain consumer authority | 21 packages in nine families, with independently selected persistence, delivery, hosting and continuations. Reinspection of package references and callable registration finds no new boundary violation requiring another package split. Bounded inbox execution remains deliberately selected and authorized; business admission, contracts, routes and process supervision stay with the consumer. This is a targeted assessment, not a complete runtime correctness audit. |
| Reduce samples; preserve distinct proofs | Wholesale retains Access, Sales and Inventory. Focused family examples and test-owned compatibility/process fixtures have current owners. Existing old sample namespaces are not dependency evidence; renaming migration metadata solely for appearance would add churn without improving the consumer contract. No further sample retirement is justified by this pass. |
| Local NuGet source for another project | Pack-only snapshots, source mapping and exact-version guidance already exist. Checkpoint evidence covers 21 archives/six isolated consumers. Remote publication is still separate; no new feed service or automation is needed. |
| Documentation/tests/empty folders | 31 native test projects have current owners, including the independent EF coordinator suite. This pass finds one broken heading link and an inaccurate ServiceDefaults dependency description. Historical payloads/migrations and private ignored areas remain untouched. The prior targeted generated-directory cleanup remains dated evidence; active build caches are not retired projects. |
| CI | Family/path/package/runtime selection remains explicit. This pass finds a missed base-package match in the EF dependency-update group. It does not change tested dependency versions or claim hosted workflow execution. |

## Concrete repairs

1. **Broken editor launch:** `.vscode/launch.json` still named and targeted the deleted
   `archive/proof-sample`. It now launches from the workspace; the existing
   [Aspire config](../../aspire.config.json) selects Wholesale's real AppHost. This follows the
   [official VS Code configuration](https://aspire.dev/get-started/aspire-vscode-extension/).
   The interactive debugger itself is not executed by this pass.
2. **Incomplete EF update group:** `Microsoft.EntityFrameworkCore.*` requires a dot after the
   base name, so `Microsoft.EntityFrameworkCore` fell into the individual-update group.
   Add that exact name beside the wildcard; retain the existing minor/patch and major policy.
   [Dependabot pattern semantics](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference#patterns-and-exclude-patterns-groups)
   support this correction. No dependency version changes.
3. **Broken tenant setup heading:** the PostgreSQL utility linked to a removed development-guide
   heading. Repoint it to the current tooling/infrastructure section. A temporary heading scan
   found the candidate, then the target was checked manually; the durable reference checker
   still has its documented existence-only scope.
4. **Incorrect shared-host description:** Wholesale's ServiceDefaults README said the independent
   Messaging hosts reused its project. They actually own a separate native composition. Correct
   the description and link it to the real source; no shared telemetry package is extracted.

The application-pattern recipe is also more directly usable for the owner's original
document-driven setup idea: first behavior, module Contracts/implementation, scoped context,
schema/migrations, explicit registrations, finite setup and failure proofs. It prescribes no
new library API or mandatory project template.

## What does not follow from this audit

No new reusable mechanism was proven. Existing source/meter wiring does not justify another
adapter, and native composition repeated in independent examples does not by itself justify
a framework. The current bounded continuation runner does not supply ERP human assignment,
deadlines, scheduling or a complete durable business-process model. Those need a concrete
Umbrine behavior before extending a public interface; they are already documented limits,
not newly discovered supported-feature defects or new deferrals.

My recommendation after these concrete repairs is to start one real Umbrine behavior using
local packages and let its integration costs identify the next library work. Further generic
cleanup or a new bootstrap skill should have a specific benefit. The owner's separate-session
sweep can still challenge these conclusions.

## Verification and handoff

Fresh checks passed:

- Parsed launch/config files resolve Wholesale's real AppHost from the workspace. All four
  centrally declared EF packages match the existing minor/patch group after adding the base
  package name. These checks do not execute the VS Code debugger or Dependabot's hosted service.
- All 77 tracked projects appear in the solution. All 21 library projects reference no sample
  or test projects; the inspected runtime registrations retain independent producer/intake,
  processor, handler, dispatcher and optional hosting choices.
- Repository tooling types/formatting and all six regression cases passed. Full local reference
  validation and whitespace checks passed. A bounded temporary heading scan matched 46 tracked
  inline local heading links after manually confirming/fixing the one original mismatch.
- Native `dotnet pack` of `Gloaming.Tenancy.EntityFrameworkCore.Postgres`, followed by archive
  inspection, contains the corrected development-guide URL and no removed heading. Package
  version: `1.0.0-audit.1792756541235`. This validates the affected README, not another full
  21-package/six-consumer execution.

Local evidence: `/tmp/gloaming-original-request-{static,tooling,headings,package}.log` and
the archive under `/tmp/gloaming-original-request-package-aorwkkv0/`. The normal checkpoint-hook
log is `/tmp/gloaming-extra-pass-commit.log`; those checks preceded this new sweep.

All **870 owned source/project/fixture inputs** under `src`, `samples` and `tests` have no diff
from `c5acb8e`. Existing library/sample/test code, migrations, fixtures and dependency versions
remain unchanged. Previous full native, package and independent-review results retain their
checkpoint scopes; this pass does not count them as new executions.

Review-worthy files are the editor profile, Dependabot group, two corrected consumer guides,
application patterns, checkpoint status and this report/queue: nine paths in total. The existing index remains
preserved; all new changes are unstaged. No new checkpoint, push or publication is performed.
