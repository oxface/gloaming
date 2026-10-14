# CI lanes by library family

Date: 2026-10-08. Base checkpoint: `d63091c`.

## Outcome and ownership

CI now reports independently named checks for each Gloaming family, alongside shared
repository integrity, Wholesale consumer composition and template adoption. Tests remain
with the capability they exercise; cross-module application policy remains in the sample
composition job. This is CI organization, not a new library interface or runtime mechanism.

| Review-worthy files | Behavior |
| --- | --- |
| `.github/workflows/ci.yml` | Replace infrastructure-oriented context/PostgreSQL jobs with a five-family matrix and a Wholesale composition job. Centralize whole-solution style/analyzers/build and architecture tests in Repository integrity alongside existing formatting, commitlint and archive checks. |
| `.github/workflows/template.yml` | Name the existing check Template integrity and adoption; cancel superseded runs. Retain generator commands, both external consumers and PostgreSQL service. |
| `.github/workflows/sample-runtime.yml` | Name the existing check Wholesale runtime. Retain filtered PR and manual triggers; remove automatic main-push execution after merge. Keep test commands, certificate preflight and all four deployment cases. |
| `README.md`, `docs/development.md` | Document all nine checks, ownership, independent project builds, triggers and branch-protection implications. Preserve local verification commands and correct outdated lane/hook descriptions. |
| This report | Record the coverage inventory, local verification and remaining hosted/performance limits. |

Core and HTTP/EF adapters share their family's check. ActorIdentity and Tenancy retain
their standalone HTTP proofs. Persistence owns EF validation and its independent GUID
consumer. Events owns serialization/upcasting/history integrity and codec adoption.
EventSourcing owns aggregate core, PostgreSQL history/rebuilding and both independent and
Wholesale event-sourcing adopters. Wholesale composition owns context wiring, module
migrations and persisted HTTP admission/business/telemetry proofs.

Every family, composition, repository and template check still runs on each PR/main push.
No family path filtering was introduced: a dependency change can affect another family.
The matrix uses `fail-fast: false`, so a failing family does not cancel siblings. Each
family restores/builds selected test projects and their dependencies on its own runner;
there is no cross-job build artifact dependency. The repository job still builds the
whole active solution. All jobs have a 15-minute limit including setup/build; repository
integrity's former five-minute limit was increased to include its additional checks.

Wholesale runtime remains sample deployment evidence. Its five-minute execution cost
is addressed by avoiding the automatic post-merge repeat, while preserving relevant PR
execution and manual dispatch. Its path-filtered check stays optional globally; use the
always-running checks for branch protection. Enabling protection is owner-controlled
repository configuration and was not performed in this change.

## Verification

The before/after command inventory retained all 19 main-workflow test/console invocations
exactly once, with all project paths resolving. The unchanged template and runtime test
commands retain their existing coverage. Actionlint validates all three workflow definitions;
the runtime trigger check confirms filtered PR/manual dispatch with no push trigger.

The family commands ran from a disposable source copy with no prebuilt outputs. Before
each family and the Wholesale composition run, its copy's `bin`/`obj` directories were
removed. This exercises restore/build from selected project graphs without relying on
the repository job. PostgreSQL suites used real containers through the local Podman
socket. Tests ran with two logical processors.

| Lane | Passed cases |
| --- | ---: |
| Gloaming.ActorIdentity | 34 |
| Gloaming.Tenancy | 56 |
| Gloaming.Persistence | 29 |
| Gloaming.Events | 89 |
| Gloaming.EventSourcing | 278 |
| Wholesale consumer composition | 148 |
| Repository architecture | 68 |

All 17 existing test projects passed: 702 cases, zero failures/skips. Both explicit
console invocations also completed. The consolidated repository commands passed restore,
style and analyzers, whole-solution build with zero warnings/errors, architecture tests
and CSharpier (408 files). Commitlint passed for the current checkpoint, and the archive
verifier matched all 800 frozen originals. All three workflows passed Actionlint 1.7.12
and YAML parsing; 132 local links in the changed documents resolved. Whitespace checks
passed. The disposable checkout and command-extraction scripts were removed.

## Limits and findings

Hosted GitHub execution and job durations require confirmation after review and push.
Parallel family jobs improve failure attribution and may reduce elapsed time, but their
separate restores/builds add runner overhead; no reduction in total runner minutes is
claimed. Build caching, artifact transfer and selective family scheduling remain deferred
until measurements warrant them. Template generation and the full browser deployment
were not rerun merely for job naming/concurrency/scheduling changes; their earlier results
remain historical evidence rather than new proofs here.

No C# implementation, library interface, business rule, database fixture/migration,
template source or frozen archive file changed. **No new reusable mechanism was proven.**
The extraction finding is organizational: family-owned executable adopters can run from
their own project graphs, while sample composition and repository architecture remain
separate responsibilities.
