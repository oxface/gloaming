# Native command-receipt experiment

Historical review-time scope: the later [extraction](command-receipts-extraction.md) replaces
the private protocol with a reusable library and closes the named extraction gates. Results
below describe the preceding native experiment, not fresh verification of that later code.

Date: 2026-10-23. Base: owner-approved checkpoint `9eaab6d` plus the preceding unstaged
[capability research](../research/wolverine-marten-opportunities.md). The owner requested
proceeding with the recommended first experiment. This batch is uncommitted in the existing
`docs/gloaming-library-audit` branch. The existing staged snapshot is preserved.

## Outcome and selected boundary

Sales now has two executable callers of one private native PostgreSQL receipt protocol:
explicitly selected draft submissions and operator compensation retries. Equal logical attempts
recover their original result after a lost response. Different input under the same scoped
identity conflicts. Another refusal can pause a workflow again without making an old operator
request a new signal. This is consumer code and evidence for a possible extraction, not a new
Gloaming interface, package or general HTTP idempotency guarantee.

The [consumer guide](../../samples/Wholesale/modules/Sales/command-receipts.md) records setup,
transaction/permission obligations, exact supported behavior and limits beside Sales. Its
[private helper](../../samples/Wholesale/modules/Sales/Sales/SalesCommandReceipts.cs) performs
transaction-lock/read/compare/decode and stages the completed row. PostgreSQL locks the full
operation scope through a stable hash; native full keys and input comparison decide identity.
Hash collisions serialize work without treating different keys as duplicates.

Only completed receipts are inserted, in the same transaction as the business result and any
next request. A claim committed before the business work is deliberately rejected: it could
strand an unexecuted action after process death. Native ReadCommitted isolation is required
to observe the winning attempt after waiting. No automatic transaction owner, retry loop,
handler discovery or module router is introduced.

## Concrete usage and compatibility

`SubmitDraftOrder` gains an optional operation identity. Omission keeps existing submission
semantics; supplying it selects receipts. The API passes it through and maps content conflict
to 409; the HTTP lifecycle proof repeats a successful versioned submission and then changes
the input under that ID. The client must retain its ID before sending. UI generation/storage
of action identities is not implemented by this consumer experiment.

`RetryCompensationAsync` now requires an operation identity, and both worker modes require
`--OperationId` for that explicit role. A new operator decision needs a new identity; callers
reuse the old one only to recover that decision. Tests and worker guidance follow this sample
Contract change. There are no external consumers requiring the discarded signature.

The forward Sales migration creates `command_receipts` with a tenant/operation/ID primary key,
format version and native text input/result. Its snapshot adds only that entity. Existing
migrations, payload literals and business rows remain; historical outcomes are not backfilled.
The restricted CQRS fixture installs receipt RLS with SELECT/INSERT rights, preserving the
runtime's inability to update/delete results. The restricted HTTP profile keeps its narrower
read/edit capability and receives no receipt rights.

## Proof ownership and limits

The new assertions are **Wholesale consumer proofs** because Sales owns the protocol. They
are not direct tests of a new Gloaming contract. Existing inbox, workflow, ownership and saving
capabilities are composed through public APIs; their family-local tests stay independent.

- [Draft receipt proofs](../../samples/Wholesale/IntegrationTests/Reactions/ReactionTests.Receipts.cs)
  cover a genuinely blocked competing writer, equal/conflicting input, cancelled waiters,
  discard before/after saving, failed required allowance reaction, commit-response loss,
  killed processes before/after commit, independent tenants and revoked resource grants.
- [Signal proofs](../../samples/Wholesale/IntegrationTests/Reservations/Local/CommandReceiptTests.cs)
  cover equal concurrent retries, an old retry after another pause, a new logical retry,
  input conflict, atomic rollback of receipt/progress/next outbox request, and invalid retained
  status values. Their explicitly admitted synthetic Inventory outcomes isolate receipt/signal
  semantics; existing real local/broker journeys retain ownership of actual Inventory effects.
- [Restricted-role proof](../../samples/Wholesale/IntegrationTests/Queries/TenantIsolationTests.Receipts.cs)
  replays an authorized result under real PostgreSQL RLS, disables EF filters, attempts a foreign
  insert and verifies that runtime replacement/deletion is denied.

The process cuts kill a real compiled child after saving or after committing, before returning
its result. A separate post-commit interceptor throws after successful database commit. These
establish recovery under those cuts; they do not simulate every network/server failure or prove
provider effects exactly once. An abandoned committed result is recoverable only while retained
and readable by the new authorized deployment.

## Fresh verification

The final solution build passed with zero warnings/errors. The complete Wholesale integration
suite passed **641 cases**, with zero failures/skips: the 622 retained cases plus 19 new receipt
cases, including the final invalid-status hardening. The existing HTTP lifecycle case is also
strengthened without adding another host/project. The final run took 3m 11s; log:
`/tmp/gloaming-receipts-final-wholesale.log`.

The first targeted run passed 72/73 cases; its one failure was a missing Beta seed in the new
fixture, corrected before the full passing runs. An intermediate complete run passed 639 cases
before two invalid-status cases were added. Neither intermediate result substitutes for the
final 641-case execution. The codec now stores known reservation-status names and rejects
integer/undefined values, alongside required-constructor/format checks.

Against the final build, all **84 architecture cases** and all **six runtime/browser cases**
passed, with zero failures/skips. Runtime ran after the integration containers were torn down,
using the existing Podman socket, installed Chromium and documented native HTTPS trust. It took
4m 40s and covered real startup/setup, database outage, OIDC/browser and restricted-RLS composition.
Logs: `/tmp/gloaming-receipts-architecture.log` and `/tmp/gloaming-receipts-runtime.log`.

CSharpier, including generated files, and read-only .NET style verification passed for all
25 changed C# paths. EF reports no pending Sales model changes. The regenerated snapshot's
only change is the new entity. Repository types/tooling formatting, all six checker regression
cases and the reference scan passed (880 Markdown targets, 296 literal project/source targets,
zero broken references). Initial sandboxed .NET test/style invocations could not open local
IPC; rerunning with that access resolved the tooling restriction.

All 15 selected Umbrine source hashes still match. Gloaming source, package/project/dependency/
solution inputs, retained migrations and payload fixtures are unchanged. No package publication,
push or commit is performed; new edits remain unstaged. Package-only adoption and the full
family-local suites are not rerun for a consumer-only change.
Existing CI path selection already includes these module/API/worker/test paths; the new cases
join the existing integration project without another project, filter or job.

## Extraction assessment and remaining work

The shared technical obligation is now executable in two Sales callers. The native helper is
small enough that retaining it as a recipe remains a credible outcome. A future interface must
remove meaningful complexity after native mapping, context selection, transaction checks,
locking deployment, codecs and failure disposal are counted. No provider-neutral contract or
separate package is justified by this experiment alone.

Consumer policy includes authorization/admission, operation identity, input equality, result
meaning, retention, module schema/migration, workflow state and business decisions. Template
setup needs no change. No new sample module or invented business journey is added. No new
reusable Gloaming mechanism is proven; the result is a proven native consumer protocol.

Receipts currently have append-only lifetime. Before selecting a general library capability,
resolve retention/removal and any pruning-versus-repeat behavior, reader-before-writer and
separately compiled deployment compatibility, result evolution and the complete adoption surface
in a second consuming context. The current unknown-format/missing-field/invalid-enum checks
do not replace those proofs. These are open extraction gates, not silently deferred obligations
of an already selected Gloaming feature. Scheduling remains a separate unimplemented candidate.

The owner's fresh-context cleanup sweep remains in the audit queue. Earlier integrated package/
browser results certify their own checkpoints; they are not evidence for this new Sales behavior.
