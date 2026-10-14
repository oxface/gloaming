# Gloaming library rename

Date: 2026-10-14. Base: freshly fetched `origin/main`, `c594325`.
The owner selected Gloaming for the reusable libraries and Umbrine for the future ERP product
built on the application template and Gloaming.
Umbrine derives from Umbral; both product names share a darkness theme.

## Outcome and review scope

All 17 library projects across seven families use `Gloaming.*` namespaces, project filenames,
assembly names and default package IDs. Source family/package directories, library test
namespaces, project references, friend-assembly declarations and architecture assertions
use the same naming. Existing test project names remain stable.

The rename includes sample/template consumers, solution paths, CI family names and path
selection, hooks, template source snapshots and omission checks, library documentation,
plans, ADRs and reports. Public OTel helpers now use `AddGloamingMessaging` and
`GloamingMessaging*Extensions`; the activity source/meter is `Gloaming.Messaging`, and
branded activity, metric and attribute names use `gloaming.*`.

Review-worthy files are:

- The seven `src/Gloaming.*` families, especially package project files, friend declarations,
  messaging instrumentation and native OTel subscription helpers.
- `ModulithFoundry.slnx`, active project references and `tests/ArchitectureTests/`.
- `.github/ci-paths.yml`, `.github/workflows/ci.yml`, `lefthook.yml` and `tools/template/`.
- Sample migration designers/model snapshots carrying the renamed EF annotation metadata.
- `README.md`, `CONTEXT.md`, `docs/design.md` and the extraction plan for the naming boundary.

Modulith Foundry remains the repository, sample and template identity. Umbrine is the future
ERP product adopting the template and Gloaming. Existing business
Contracts, database/schema/table identifiers, event aliases/schemas and wire messages retain
their identities. The older `ModulithFoundry:` EF annotation keys also remain unchanged.

Branded EF annotation keys now use `Gloaming:` in both runtime mapping/validation and active
migration designers/model snapshots. These are model metadata, not stored data. No migration
operation or persisted event is rewritten, and no new migration is introduced.

The [initial naming checkpoint](library-naming-checkpoint.md) retains the original dated
results under a neutral filename. Historical reports use current library/type/path spellings
for navigation; their dates, checkpoint references and test results remain historical.
Original spellings remain available in Git history. The frozen archive is unchanged.

## Current verification

These are new executions against the renamed source, separate from earlier checkpoint
results:

- Restore and full solution build: all 67 active projects, zero warnings/errors.
- All 27 non-browser test suites: **1,001 passed, zero failed or skipped**. These include
  all seven library families, architecture/independent adoption, native migrations and
  model consistency, PostgreSQL transactions/concurrency, RabbitMQ, worker processes and
  exported telemetry assertions.
- T1 creation proof: both external consumers restored/built and passed their five tests
  each against disposable PostgreSQL 18.6. Deterministic snapshots, configurable naming,
  local references, omission and initial-creation failures also passed.
- Context and event-codec console consumers both ran successfully.
- Native style, analyzers and CSharpier checks passed, as did template TypeScript type
  and formatting checks. `git diff --check` passed.
- Evaluated MSBuild properties confirm matching Gloaming assembly names, root namespaces
  and package IDs for all 17 library projects.

The structural audit compared every base file with its current path: code/project/tooling
bodies differ only by the rename, import ordering and formatter whitespace. It accounts for
253 renamed files and 540 changed existing file bodies, plus this new report. All 113 other
tracked migration files are byte-identical; 17 migration designers/snapshots change only
identity references/annotation metadata and formatting. Existing model-consistency tests
report no pending schema changes.

No previous-brand reference remains in tracked content or filenames. All 1,598 active local
Markdown links resolve. The archive verifier matches all 800 original frozen files. The
index checksum is unchanged from before the rename, preserving the owner's review snapshot.

## Mechanisms, policy and limitations

**No new reusable mechanism was proven.** This increment exercises existing mechanisms under
new source identities. It changes no library capability, template composition or sample-owned
admission, domain decisions, transaction ownership, transport routing or deployment policy.
No new extraction candidate or library interface is proposed.

External consumers must update namespaces/project or package references, rebuild, and update
OTel subscriptions, instrumentation names and dashboard queries. Consumers retaining native
model snapshots with the previous branded annotation keys must update those metadata keys
alongside their library upgrade. There is no compatibility facade or package publication.

Aspire/browser runtime suites and archived suites are not rerun for this mechanical rename.
Active hosts compile through the full solution build. Existing provider, deployment and
deferred-capability limits remain unchanged. All changes remain unstaged; no commit or push
is part of this increment.
