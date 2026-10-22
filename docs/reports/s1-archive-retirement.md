# S1 archive retirement

Date: 2026-10-22. Branch: `feat/v1-integration`.
Scope: [selected S1 retirement](../plans/s1-sample-experiment-consolidation.md#archive-retirement-after-the-selected-slices).

Status: archive retirement implemented after the coordinated functional/template prerequisite
gate. Canonical Directory integration passed 713 product cases, 108 RouteKeys provider cases,
114 architecture cases and its 83-project build in its isolated checkpoint before this gate;
its final primary integration remains coordinated root work. Selected T2 generated consumers
passed 23 cases; ordinary package adoption passed 23 archives and four consumers before the
final routing/package rerun. These are separately recorded prerequisite executions, not tests
rerun by this retirement. Final independent code/P2 review and release verification remain open.

## Provenance and exact scope

The original source revision is
[`15d1ec6ce85f872e4547b37c33658dea76b25d03`](https://github.com/oxface/modulith-foundry/tree/15d1ec6ce85f872e4547b37c33658dea76b25d03).
It is an ancestor of the fetched integration baseline
[`790a8b76b7a519039f61434f330d2ed1b07d70e9`](https://github.com/oxface/modulith-foundry/tree/790a8b76b7a519039f61434f330d2ed1b07d70e9).
That baseline retains the
[archive guide](https://github.com/oxface/modulith-foundry/blob/790a8b76b7a519039f61434f330d2ed1b07d70e9/archive/README.md)
and [800-file manifest](https://github.com/oxface/modulith-foundry/blob/790a8b76b7a519039f61434f330d2ed1b07d70e9/archive/proof-sample/SNAPSHOT.json).
Historical source links use the original revision and original paths; archive metadata uses
the baseline revision. Both are immutable Git object references rather than moving branches.

The implemented deletion set is the **802 tracked files** beneath `archive/`: 800 original source
files, `proof-sample/SNAPSHOT.json` and the archive guide, plus the obsolete
`tools/repository/verify-archive.py`. Only these tracked files were removed; unrelated
untracked/ignored local artifacts were preserved. No replacement frozen copy or reconstruction
patch was created. Historical source, commands and dated findings remain available at the
revisions above; they are not active implementation
instructions or evidence of a fresh runtime execution.

The accompanying reference/tooling map is:

| Owner | Selected disposition |
| --- | --- |
| Root README, development guide, repository tooling guide | Replace local archive commands/links with current owners and immutable provenance. |
| Historical plans/reports and Events fixture provenance | Replace relative archive links with verified immutable original source targets; preserve dated results and their historical meaning. |
| `AGENTS.md`, repository workflow | Replace archive-preservation wording with Git provenance and active fixture/migration ownership. |
| Main CI workflow | Remove only the checksum-verifier step; retain all active integrity/consumer checks. |
| CI path map, Dependabot, CSharpier | Remove archive-only exclusions after removing their tracked source. |
| Template proof | Keep the forbidden archive/dependency guard: it also protects active generated consumers. |
| Package workflow, reusable changes workflow and packages path-map section | Keep current package adoption/selection checks. |

## Retained evidence and ownership

The [Wholesale consolidation report](s1-wholesale-consolidation.md) records the complete
product proof transfer and runtime ownership map. This retirement adds no replacement
implementation and removes no active migration, literal fixture, test or consumer entry point.

| Historical obligation | Current owned evidence/disposition |
| --- | --- |
| Persisted event payload compatibility | [Events literal fixtures](../../src/Gloaming.Events/samples/EventCodecDemo/Fixtures/README.md), independent codec/history/upcasting tests and Wholesale `IntegrationTests/EventPersistence`. The two original Inventory literals remain byte-identical; Purchasing and later-version literals retain their documented independently authored origin. |
| Module schema/migration isolation and historical Access upgrades | Family-local Persistence examples/tests, Wholesale module `Migrations` directories and `IntegrationTests/Api` migration/SQL-fixture proofs. Current migrations remain consumer-owned; archived migrations are historical comparisons rather than executable active dependencies. |
| Expected-version append, required inline state, outbox atomicity, history and rebuilding | EventSourcing family tests/examples, Wholesale `IntegrationTests/EventPersistence` and actual Worker maintenance commands. Consumer reducers, required views, admission and transaction ownership stay explicit. |
| Inbox/outbox identity, retries, leases, rollback, broker recovery and shutdown | Messaging family contracts/examples/tests and Wholesale `IntegrationTests/Reservations`, with broker/local roles in the canonical Worker. Historical aggregate/workflow policy is not imported into libraries. |
| Access/admission, permissions, RLS and accepted-change audit | Canonical Wholesale API, separately owned module Contracts/persistence and corresponding integration proofs; independent identity/tenancy/auditing family proofs remain. Business authorization and credential/policy provisioning remain consumer-owned. |
| DDD, reactions and CQRS journeys | Wholesale modules, actual API/Worker operations and consolidated Domain/DraftOrders/Reactions/Queries proofs. Test-owned scenarios retain finite historical journeys without becoming product dependencies. |
| Template omission/selection and independent adoption | [T2 generated consumers](t2-template-patterns.md) and package-only adoption checks; generated applications do not restore or read archive projects. |

The two retained Inventory payload SHA-256 values are:

| Active fixture | SHA-256 |
| --- | --- |
| `src/Gloaming.Events/samples/EventCodecDemo/Fixtures/Inventory/opened.v1.json` | `a5f6294f14afcee9b10f825be292d2ddd018834d3c7c8402a48671830d99d2d2` |
| `src/Gloaming.Events/samples/EventCodecDemo/Fixtures/Inventory/received.v1.json` | `feece448f1623f9a350995e714cee156d22ca22fbd06d3b80cae8835266c433a` |

## Verification and limits

Before retirement, the archive verifier passed for all 800 original files. Independent comparison
against the original Git tree also matched all 800 manifest paths, byte counts and SHA-256 values;
native Git ancestry inspection confirmed that revision precedes the pinned archive baseline.
A separate tracked-file inventory counted exactly 802 archive files. The immutable-link assessment
found 87 relative archive references across 15 tracked Markdown files and verified every mapped
target with native Git object lookup. Historical source references now use those immutable
targets; the active README points readers to this retirement record.

Fresh checks after retirement:

- Native Git diff confirms exactly **803 tracked deletions**, matching the 802-file archive
  safelist and checksum verifier, with no other tracked deletion.
- All **131 active fixture/migration data and source files** in the preservation baseline remain
  byte-identical across families, Wholesale and templates. Its additional fixture README changes
  only provenance links. The two original Inventory JSON files also match original Git blobs
  and the SHA-256 values above.
- Tracked active source, sample, test and template source/project/config files have **zero
  archive dependencies**. There are no remaining relative archive Markdown links.
- A full tracked Markdown scan checked **2,982 local targets**. Its only seven missing targets
  are the known P1 links to former CQRS/event-persistence paths; their corrections are already
  on the incoming canonical Directory branch and must be rechecked after that merge. No missing
  archive target remains. This scan checks local files; immutable historical links were separately
  validated against native Git objects.
- Node 24 native glob comparison preserves local selection for **1,343 active tracked paths
  across 15 filters**. The only path-map changes remove two archive exclusions. The `packages`
  filter, package/changes workflows and active template dependency-rejection guard are unchanged.
  This is a local comparison, not execution of GitHub scheduling.
- `git diff --check` passed; the initially empty index remains empty. No commit or push was
  performed by the retirement worker.

These are fresh provenance/source/tooling checks; they do not run or certify the archived
application. No active C# implementation changed in this retirement, so it adds no new runtime
or provider test execution. Final integration executes the affected complete graph after merge.

The [Wholesale report](s1-wholesale-consolidation.md) and [T2 report](t2-template-patterns.md)
retain their own dated executions and exact limitations. Their earlier passes are prerequisite
evidence, not newly executed retirement results. Integrated runtime/architecture/code-review
results must be recorded against the final cleaned tree. No historical test count substitutes
for those checks.

## Findings and remaining work

**No new reusable Gloaming mechanism was proven or added.** Library guarantees remain with
their existing family-owned contracts and tests. Template setup remains editable consumer
composition; Wholesale business policy, schemas, migrations, payload choices and operational
admission remain consumer-owned. Historical extraction candidates remain comparisons and
proposals unless their owning active work explicitly implemented/reviewed them.

Remaining required work: merge the canonical Directory checkpoint and its seven known P1 link
repairs, rerun final integrated/package verification, then complete independent code and
architecture/isolation reviews plus release audit on the cleaned tree. Root integration tracking
owns final block/release status. Archive retirement itself adds no unresolved functional proof gap.
