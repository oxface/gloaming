# Command-receipt capability extraction

Date: 2026-10-23. Base: approved checkpoint `9eaab6d`, followed by the unstaged
[research](../research/wolverine-marten-opportunities.md) and [native experiment](command-receipt-experiment.md).
The owner requested closing retention, deployment and extraction gaps before moving to timers.
This is an implemented public-interface review candidate in the same branch; owner line-by-line
review and exact commit approval remain pending. No commit, push or publication is authorized
by implementation checks.

## Outcome and adoption surface

`Gloaming.CommandReceipts.EntityFrameworkCore.Postgres` is independently selectable from
Messaging, tenancy, actor context and sample business types. It adds one package/family, giving
22 packages in ten families. Its [owning contract](../../src/Gloaming.CommandReceipts/Gloaming.CommandReceipts.EntityFrameworkCore.Postgres/README.md)
defines native setup, guarantees, supported limitations, retention and deployment behavior.

The mechanism owns full-key transaction serialization, exact retained-input comparison,
immutable completed-result staging and native save guards. Consumer authority includes admitted
scope, permissions, logical operation identity, comparison bytes, result version/readers,
schema/table placement, native migration/credentials and transaction save/commit. Acquisition
and Retain are explicit operations; no handler execution runtime, callback pipeline, mandatory
host, default serializer or transport is added.

The [public store](../../src/Gloaming.CommandReceipts/Gloaming.CommandReceipts.EntityFrameworkCore.Postgres/PostgresCommandReceipts.cs)
requires one attempt per fresh context and an active native ReadCommitted transaction.
[Attempt](../../src/Gloaming.CommandReceipts/Gloaming.CommandReceipts.EntityFrameworkCore.Postgres/CommandReceiptAttempt.cs)
stages a completed receipt only on a new accepted decision. [Native save validation](../../src/Gloaming.CommandReceipts/Gloaming.CommandReceipts.EntityFrameworkCore.Postgres/ReceiptScope.cs)
rejects changed/detached/directly modified receipts and transaction-separated proposals. Existing
EF capability registration is optional composition; explicit native save overrides also work.
The provider-specific dependency is deliberate; no unproven provider-neutral layer is introduced.

## Closed gaps and chosen limits

**Retention is a contract, not a new worker.** Retain receipts for module lifetime; no clock,
TTL, automatic expiry or prune API exists. Runtime writes are append-only. Explicit native owner
removal ends protection and can let an admitted old identity execute again; a direct proof
exercises that boundary. Retire admission before evidence removal when re-execution is forbidden.
Result/reference content remains consumer data policy. No missing permanent registry is assumed.

**Deployment compatibility has executable evidence.** Separately compiled Earlier/Current
consumers have different request/result DTOs. Current keeps meaningful V1 comparison bytes,
reads V1/V2 and writes V2 for new actions. Earlier refuses an unsupported retained V2 result
without executing again; restoring a compatible reader recovers it. Overlapping old/new processes
coordinate through the native lock until commit. Killing an old process before/after commit
recovers one business effect in the replacement. A frozen native lock vector independently
protects V1 framing. These are consumer upgrades using the first library implementation;
there is no earlier released receipt library version to certify.

The first public contract fixes six scalar columns, full scope/operation/GUID key, ordinal
key/input equality, opaque versioned result and V1 lock frame. Consumers own placement and
native migrations. Future releases must preserve these meanings/coordination or supply an
explicit compatible upgrade. Unknown consumer result formats fail in the consumer reader;
they must never be treated as an absent receipt. No universal DTO-upcaster is selected.

**Extraction has independent adoption.** A different publishing DbContext uses only the public
receipt package, native EF/Npgsql and DI. It commits publication plus its own plain native
dispatch-intent row with the receipt. Neither that adopter nor deployment fixtures reference
Sales or Messaging. The package-only profile independently uses native save overrides outside
the repository, without ProjectReferences. Sales retains only its codec/exception adapter and
composes real permissions, RLS, reactions and workflow/outbox writes through the library.

## Consumer, migration and test ownership

The original Sales prototype migration is preserved. A forward UseLibraryCommandReceipts
migration retains data and sets deterministic C key collation. The snapshot replaces the private
record type with the provided technical type, preserving physical column names. Populated-table
upgrade coverage checks original result recovery and one allowance/business effect. Prototype
writers must drain because their uncommitted private lock scheme predates public V1; existing
prototype provisioning must handle dependent policies around column changes. No external
consumers exist, and no released upgrade guarantee is asserted for the private experiment.

The family suite directly tests the new public contract. Wholesale tests remain application
integration proofs; fixture scope is explained in the [test guide](../../tests/README.md).
The public package count is 22, native test-project count 32 and isolated package-profile count
seven. CI has an explicit family lane/path selection, architecture dependency declarations and
package-only adoption; no new sample domain/module or template generator is introduced.

No expiry framework, generic command executor, global idempotency middleware or automatic
infrastructure provisioning follows. The selected reusable mechanism is the PostgreSQL
lock/compare/retain/save protocol. Business intent, payload evolution and authority stay with
the consumer. The native experiment's earlier absence of a reusable mechanism is superseded
by this candidate and its independent proofs, pending owner review.

## Fresh verification and review

The extraction was verified locally on 2026-10-23; the experiment report's earlier counts are
dated evidence, not results of this extraction. Normal solution restore/build passed with zero
warnings/errors. The independent family suite passed **30 cases**, including the frozen lock
vector, separate-process deployment cases and identifier-truncation rejection. Architecture
passed **86 cases**, including the new declaration and assembly boundaries.
The complete Wholesale integration suite passed **642 cases**, including populated migration,
real permissions/RLS and receipt composition with reactions and durable continuation writes.
Native EF reports no model changes since the last Sales migration.
All **six runtime/browser cases** passed after the provider/package containers finished.
These executions had zero failures/skips; unrelated family suites were not rerun in this slice.

The isolated NuGet verifier passed **22 archives and seven consumer profiles**, with no
ProjectReferences and a fresh package cache. Its receipt profile exercised actual PostgreSQL
save/commit and replay using explicit native save overrides. Final evidence is
`/home/oxface/.cache/tmp/gloaming-package-proof-5g9fbI/manifest.json`, SHA-256
`02b27dd95d35eb34c2d946c4a8b2be489ba349b576115e5b80c9c0cdffdf16d5`.
The manifest and logs are local artifacts, not committed fixtures or publication evidence.

Targeted native style/analyzer checks and CSharpier (48 changed C#/project/solution files,
including generated inputs) pass. CI YAML parses, with matching family filter,
workflow outputs and execution lane; no hosted CI run is claimed. Repository/release tooling
pass: six repository-tool cases, 902 local Markdown targets and 313 literal project/source
targets, with zero broken references. The original two prototype migration files match their
preceding review snapshot; older tracked migrations/fixtures and all 15
read-only Umbrine reference documents are unchanged. The staged snapshot is preserved.

The first architecture attempt exposed a missing Inventory assembly in the test's dependency
loader; adding it made the intended exclusion check executable and the suite pass. A first
package-verifier attempt stopped at the deliberate 21-package inventory gate; the reviewed
inventory is now 22. Sandbox restore/formatter failures required normal network/named-pipe
access; audits and checks were not disabled.

Review the public key/content/record, Acquire/Retain protocol, model/save integration and owning
README together. Review Sales's codec/admission/transaction usage, populated migration proof,
independent adopter/deployment fixtures and release/CI declarations alongside them. These
changes prove a new reusable technical mechanism; sample permission/business policy and
application setup remain consumer-owned. Owner line-by-line review, exact checkpoint approval
and the planned independent fresh-context sweep remain outstanding. No timer capability,
additional module or new supplied-feature deferral is introduced.
