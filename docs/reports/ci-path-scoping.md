# CI path scoping with a standard action

Date: 2026-10-09. Base: `4a38dff`, the merge of documentation PR #4.
Changes remain unstaged on `fix/ci-path-scoping`.

## Outcome and cause

The earlier family split organized checks but deferred selective scheduling. Main CI and
Template creation still had unconditional test jobs. The roadmap PR therefore ran unrelated
library, database, composition and generated-template proofs.

The owner selected an existing action rather than maintaining custom selection scripts.
All three workflows now call one reusable scope job using `dorny/paths-filter` v4.0.3,
SHA-pinned to `ceb8a2b8f2d89434be7ff52d3de7ec3738c5cc9d`. One YAML file maps lane inputs,
including dependent consumers, linked fixtures and template source snapshots. The action
handles changed-file discovery and matching. No custom project parser, selector, selection
test suite or added npm dependency remains.

Markdown-only changes omit .NET, PostgreSQL, RabbitMQ, generated-template and browser work.
Repository integrity keeps commitlint and frozen-source checksums. Relevant implementation
changes additionally run its existing native build/style/analyzer/architecture checks.

Families have individual job conditions and shared YAML steps instead of a matrix. All
seven existing family check names remain; unrelated jobs skip without allocating runners.
Wholesale, template and browser jobs also use conditions. Caller workflows still start,
so required checks receive results. GitHub distinguishes skipped jobs from an entirely
path-filtered workflow, whose required checks may remain pending.
[Conditional jobs](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-jobs-with-conditions),
[workflow filters](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow#using-filters-to-target-specific-paths-for-pull-request-or-push-events).

## Concrete scope and ownership

| Files | Behavior |
| --- | --- |
| `.github/ci-paths.yml` | Explicit family/consumer/template/runtime inputs, Markdown exclusions, shared build inputs and conservative coverage for paths outside known roots. |
| `.github/workflows/changes.yml` | SHA-pinned action, read-only PR API permissions, reusable flags and manual full verification; no npm install or repository script. |
| `.github/workflows/ci.yml` | Keep family names and commands; gate jobs and expensive repository steps. Fail required integrity if scope detection fails. |
| `.github/workflows/template.yml` | Gate PostgreSQL/generated adoption and add manual full verification. |
| `.github/workflows/sample-runtime.yml` | Gate runtime at job level, omitting sample READMEs; retain no automatic post-merge push and all suite assertions. |
| Root README, development guide and repository tooling README | Describe the selected action, explicit map and maintenance obligations. |

The action's `some-with-excludes` mode makes the Markdown exclusion apply across each
filter's positive patterns. PR detection uses its paginated GitHub API; main-push detection
uses Git and the prior pushed commit. Both follow upstream behavior rather than local diff
code. Read-only contents/PR permissions are granted; no write permission is added.
[Action contract](https://github.com/dorny/paths-filter/tree/ceb8a2b8f2d89434be7ff52d3de7ec3738c5cc9d).

Manual dispatch, shared build/toolchain/project/CI changes, or inputs outside mapped roots
request full coverage. Detection/configuration failures block required Repository integrity
instead of silently accepting omitted work. This repair itself changes workflows, so its PR
intentionally runs full coverage; later documentation-only PRs use the lightweight route.

Template filters include the three current library source snapshots copied by the generator.
Runtime filters retain Wholesale implementation/shared-input/manual policy. Library-only
changes use focused contract/consumer proofs. Archive-only edits still invoke checksum
validation and never schedule archived tests.

## New verification

- Actionlint 1.7.12 passed for all four workflows, including reusable outputs, read permissions,
  expressions and YAML aliases. YAML files parsed successfully; whitespace and local links passed.
- Compiled the pinned upstream Filter implementation in temporary storage with its exact matcher
  dependency versions. Configuration probes passed for the roadmap files, family-local/sample
  docs, archive-only and empty changes, dependent consumers, shared fixtures, template/runtime
  inputs, unrelated library tests, build inputs, new roots and more than 300 supplied paths.
  No probe harness or action source was added to the repository.
- Executed the actual pinned action bundle against the merged roadmap Git range
  `18a76f8..20ec4b1`: it detected six changed files and returned false for all thirteen raw
  filters. This exercises the action's Git path; hosted PR/API behavior still needs CI confirmation.
- The previous before/after command comparison established that all seven family names and
  commands, plus Wholesale and architecture commands, remain identical. Their commands were
  not changed during the action replacement. Template/runtime suite commands are also unchanged.
- The original repository tooling manifests are restored exactly; added TypeScript/YAML/formatter
  dependencies, scripts and configuration are removed. The existing archive verifier matched
  all 800 frozen originals. The index snapshot is unchanged and all edits remain unstaged.

Fetched `origin/main` contains every pre-existing local branch tip: outbox, inbox, audit and
roadmap. No prior local work was missing from the merge. Local `main` was fast-forwarded to
`4a38dff`; the fix branch started there without an upstream. No remote push or commit was made.

## Limits and extraction finding

Dependency discovery is intentionally manual. Update the path map with changes to project
references, linked fixtures, new consumers or template snapshots. New files inside known
roots follow their configured rules; the fallback does not inspect their build semantics.
Markdown used as future executable fixture data would require an explicit exception.

Hosted scheduling, PR/API behavior, ruleset interaction and runner-minute savings await the
reviewed PR. Scope setup still runs in each caller workflow, even if all tests skip. The
removed custom script's regression results do not establish guarantees of the selected action.

Unchanged .NET/database/broker/browser suites were not rerun for scheduling configuration.
Their earlier CI passes remain historical evidence. No library interface, domain rule,
consumer transaction, template source, migration or frozen archive file changed.
**No new reusable runtime mechanism was proven.** This is repository-owned CI configuration
using an existing action, not a Gloaming library or template runtime dependency.
