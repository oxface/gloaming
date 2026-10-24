# Sales consumer capabilities

Sales owns customer profiles, drafts, customer allowances and its durable stock workflows.
Business Contracts contain ordinary intent/result/query types. Native module persistence,
grants, business rules and transactions remain consumer-owned.

The [command-receipt consumer](command-receipts.md) adds safe equal retries for explicitly
selected draft submissions and operator compensation retries. Gloaming's independently selected
CommandReceipts package owns the technical protocol; Sales owns permissions, logical identities,
input/result meaning and native transaction composition.

## Customer profiles

`AddCustomerProfiles` registers the native profile service and its explicit accepted-change
audit. It does not register an outbox publisher or a directory export/listing service.
Profile name, address and audit commit in the existing service-owned transaction. Failed
saves roll back the operation; a retry uses a fresh scope. The profile version remains the
native concurrency token for profile changes.

Dispatch's public directory and Sales listing/publication policy are retired. The forward
`RetireCustomerDirectory` migration drops only `delivery_listed`; all earlier Sales migrations
remain. Profile/address data, audit records and existing outbox envelopes are preserved.
Retired directory envelopes are not automatically deleted, redriven or consumed. `Down`
recreates a default-true column; it cannot restore old listing decisions. Workflow outbox
registration and dispatch remain explicit in the stock/reservation compositions.

## Private draft access

Register `AddSalesDatabase`, `AddDraftOrderQueries` and/or `AddDraftOrderCommands` with trusted
scoped actor/tenant accessors. Both draft registrations install the focused
`IDraftOrderAuthorization` implementation. The core Gloaming identity/tenancy libraries gain
no dependency on Access, EF permissions or FGA. Apply module migrations explicitly.

Sales grants Read or Edit for an Organization/draft and a human/system kind+key. Edit includes
Read. Query entry points require an identified actor and selected Organization and intersect
grants with tenant/business filters before detail/line loading, counts, totals and paging.
The grant table's tenant-inclusive FK prevents pointing at a draft in another Organization.
Overlapping grants use SQL EXISTS and do not duplicate resource results.

Existing-resource commands require a human, selected tenant and caller-owned native Sales
transaction, then select the target under Edit permission. Inaccessible/missing targets both
return NotFound. Edit permits submission subject to the existing aggregate/allowance rules.
Creation remains available to a trusted admitted human and stages an Edit grant for that
creator alongside the new draft. `Changed` is a proposal until the caller saves and commits;
rollback removes draft and creator grant together. Queries use database state, not unsaved
tracked grants/proposals.

`IDraftOrderAuthorization.IsAllowedAsync` supports late host checks, but does not substitute
for the checks already inside query/command entry points. Actor and tenant come from the
immutable operation, never method arguments controlled by a request. The host admits current
membership for fresh human operations and supplies explicit mandates for system work.
Membership administration and human initiator attribution confer no draft permission.

Grant checks follow native statement/transaction snapshots. Count and page use the same
predicate in two statements; choose a caller-owned RepeatableRead transaction for a common
snapshot, accepting older grant facts until it ends. A command decision is not a permission
fence at commit. Do not share a DbContext between concurrent branches or rebind contexts.

Migration creates an empty grant table and preserves old drafts without granting anyone
access. `DraftOrderAccessDemoSeed` is finite explicit setup for the HTTP demo, not a grant
editor, production backfill or reconciliation API. There is no runtime grant administration,
grant-change audit, role/group model, export/AI endpoint or automatic retry.
Privileged/raw/bulk writes and code bypassing these Contracts can bypass business protocols.
Draft visibility governs the lines/customer display fields returned here; independently
protected fields need their own selected capability and proof.

The separate [opt-in PostgreSQL RLS consumer](../../IntegrationTests/Scenarios/Cqrs/TenantIsolation/README.md)
adds database tenant equality on six Sales tables, including append-only command receipts,
under restricted credentials and explicit transaction-local setup. It proves reads/existing-draft
edits and receipt replay; ordinary module registration,
default HTTP, creation/submission and workers do not adopt those credentials/policies. It supplies
no within-tenant permission, authentication or protection from tenant-setting forgery by
the credential holder.

The optional [Tenancy/PostgreSQL extension](../../../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md)
establishes transaction-local context explicitly. The executable owns that adapter reference
and invokes it inside each native transaction; Sales permissions remain module-owned.

The separate [Sales HTTP RLS composition](../../Api/README.md#optional-sales-http-row-security)
now calls `AddSalesDatabase(connection, useRowSecurity: true)`. A stateless native EF model
customizer selects six existing ownership mappings with `.WithRls`, including profile audit;
EF caches this model separately from ordinary Sales options. No relational table/column or
snapshot changes. Registration installs the optional capability and `UseRls` once.
This mode requires caller-owned transactions for all EF commands, including queries and
authorization checks. Its finite host installer supplies reviewed policies and restricted
credentials; it does not enable grant administration, draft creation/submission, transport
or workflow rights. The normal registration remains filter/save-validation based.

The separate [reservation reply processor](../../Worker/Broker/sales-inbox-rls.md) uses
`AddSalesReservationReplyDatabase` and `AddAdmittedStockReservationReplies` with a host-established
immutable Organization/system-actor scope. The inbox processor still owns its native transaction,
saves and commit. A different stateless model selects workflow/inbox RLS and keeps named ownership
filters; the admitted handler checks existing context rather than initializing it again. The
original handler continues to establish context from an admitted message before the same workflow
continuation logic. Its existing `AddStockReservationOrders` registration remains available.
This consumer profile adds no Gloaming interface, schema migration or general worker bootstrap.

## Executable usage and evidence

- [HTTP identity/admission](../../Api/README.md#private-draft-permissions) composes late resource checks, queries and antiforgery-protected versioned edits.
- [CQRS consumer](../../IntegrationTests/Scenarios/Cqrs/README.md) exercises complete permitted selection and native snapshot timing.
- [Optional RLS setup](../../IntegrationTests/Scenarios/Cqrs/TenantIsolation/README.md) exercises restricted-role enforcement and its credential limits.
