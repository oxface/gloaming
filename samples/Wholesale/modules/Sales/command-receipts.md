# Sales command receipts

Sales adopts [Gloaming.CommandReceipts.EntityFrameworkCore.Postgres](../../../../src/Gloaming.CommandReceipts/Gloaming.CommandReceipts.EntityFrameworkCore.Postgres/README.md)
for cooperating-writer serialization, retained comparison and immutable result staging.
[SalesCommandReceipts](Sales/SalesCommandReceipts.cs) now owns only the Sales codec and translation
of technical conflicts into the sample Contract exception. Admission, permissions, result/input
meaning, operation identity and native save/commit remain consumer code.

## Explicit callers

- Draft submission accepts `SubmitDraftOrder(id, expectedVersion, operationId)` or HTTP
  `POST .../draft-orders/{id}/submit` with `{ "expectedVersion": 1, "operationId": "..." }`.
  Omission selects existing unreceipted submission. The client retains an ID before sending and
  reuses it after an uncertain response. Current UI flows do not automatically retain IDs.
- `RetryCompensationAsync(orderId, operationId, token)` requires an operation ID. Both local and
  broker workers accept `--role retry-compensation --OrderId ... --OperationId ...`. A deliberate
  later operator decision needs a new ID; order/workflow identity alone is not action identity.

The key is the admitted Organization, a stable Sales operation name and logical action ID.
Submission comparison includes draft ID/expected version; retry comparison includes order ID.
These small V1 values use an explicit JSON representation compared ordinally. Result V1 uses
the existing business DTO with required constructor fields and known reservation-status names.
This is consumer encoding, rather than arbitrary JSON equivalence selected by the library.

## Composition and transaction

`AddSalesDatabase` explicitly registers receipt save validation with the existing capability
coordinator. Its model selects `sales.command_receipts`, maps the provided record's ScopeKey
to `organization_key`, and composes existing tenant ownership/filtering. Native migrations and
storage selection remain in Sales. The library imports no Sales, Tenancy, Persistence or
Messaging types.

Draft submission queries the current Edit grant before receipt acquisition. Inaccessible targets
remain NotFound even when a retained result exists. The sample operator path retains its existing
admitted Organization/identified-actor mandate; production operator authorization remains the
consumer's. Grant evaluation follows the native statement snapshot, not a new permission fence
at commit. A retained result itself grants no authority.

Inside the caller's ReadCommitted transaction, Acquire waits for any same-key writer and exposes
the original result or a new attempt. A new accepted business proposal retains its result before
the caller saves. Draft rejection leaves no receipt. Required allowance reactions, workflow
progress and any next outgoing request commit with the receipt in that same native transaction.
No completed receipt exists after rollback. A competing equal retry can then recover the original
result or make the decision if the owner rolled back.

Draft callers still own save/commit; Changed remains a proposal until committed. The HTTP adapter
supplies that boundary. The reservation service explicitly owns its existing transaction. Use
one acquired command per fresh context, and dispose the transaction/scope after any failure,
cancellation or uncertain commit. The helper does not retry or commit native work.

An old operator retry arriving after another refused release pauses the workflow again returns
the original retry result, without invoking the newly pending signal. That result can be older
than current progress; query current state separately. A new logical decision uses a new ID.
Generic workflow SignalAsync retains its existing contract; this is deliberate composition.

## Retention, RLS and deployment

Sales retains command receipts for its module lifetime, without expiry or automatic pruning.
Explicit owner removal ends protection; old admitted IDs can execute again. Retire admission to
old actions before removing evidence if re-execution is forbidden. Storage and protected result
content are consumer operational choices; a retained reference can replace a full DTO when its
meaning remains retrievable. This slice does not add a TTL/pruning capability.

The restricted [CQRS fixture](../../IntegrationTests/Scenarios/Cqrs/TenantIsolation/README.md)
installs native receipt tenant policies and SELECT/INSERT-only runtime rights. Foreign writes
and runtime replacement/deletion fail. The narrower HTTP RLS profile receives no new receipt
privileges. Native policies, grants and credential-holder trust remain consumer configuration.

The original AddSalesCommandReceipts migration remains. The forward UseLibraryCommandReceipts
migration preserves its rows and adds deterministic C collation to the scope/operation key
columns; the snapshot selects the provided record instead of the private prototype type.
The populated upgrade proof verifies retained result recovery and unchanged allowance/business
state. No historical outcome is inferred or backfilled.

The private uncommitted prototype used another lock hash. Drain prototype writers before this
upgrade; it is not a released library protocol to mix with V1 writers. If prototype RLS policies
were installed, their owning provisioning must remove/reinstall dependent policies around native
column changes. New deployments start with the library's documented V1 frame. Preserve that
frame, schema placement, comparison encoding and result readers in future rolling deployments.

Sales continues to read/write its result V1. The independent compiled deployment consumers prove
the general reader-before-writer protocol, additive request changes and fail-closed rollback;
they do not migrate Sales business DTOs. Unknown Sales formats and invalid retained enum fields
fail without signaling or changing an allowance. Neither receipts nor deployment proofs promise
exactly-once provider effects or reproduce HTTP status/header/body bytes.

## Proof ownership

[Draft proofs](../../IntegrationTests/Reactions/ReactionTests.Receipts.cs) cover concurrency,
conflict, rollback/save failure, cancellation, real process death, uncertain commit, permission
revocation, tenancy and the populated migration. [Signal proofs](../../IntegrationTests/Reservations/Local/CommandReceiptTests.cs)
cover atomic receipt/progress/outbox, concurrent retries, an old retry after a new pause and
invalid retained states. The [restricted-role proof](../../IntegrationTests/Queries/TenantIsolationTests.Receipts.cs)
uses actual PostgreSQL RLS. The HTTP lifecycle case exercises request selection and 409 mapping.

These are Sales integration proofs. The [independent family suite](../../../../src/Gloaming.CommandReceipts/tests/CommandReceiptTests/CommandReceiptTests.csproj)
and package-only adopter directly protect Gloaming's public contract. See the
[extraction report](../../../../docs/reports/command-receipts-extraction.md) for fresh execution,
review scope and remaining supported limits.
