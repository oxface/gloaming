# Sales consumer capabilities

Sales owns customer profiles, drafts, customer allowances and its durable stock workflows.
Business Contracts contain ordinary intent/result/query types. Native module persistence,
grants, business rules and transactions remain consumer-owned.

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

The separate [opt-in PostgreSQL RLS consumer](../../CqrsDemo/TenantIsolation/README.md)
adds database tenant equality on five Sales tables under restricted credentials and explicit
transaction-local setup. It proves reads/existing-draft edits; ordinary module registration,
HTTP, creation/submission and workers do not adopt those credentials/policies. It supplies
no within-tenant permission, authentication or protection from tenant-setting forgery by
the credential holder.

The later [A2.3 utility extraction](../../../../docs/reports/a2-3-postgres-tenant-context.md)
replaces the executable's private setup helper with an optional Tenancy/PostgreSQL extension.
Sales module registration and permission semantics remain unchanged; the executable owns
that adapter reference and calls it explicitly inside each native transaction.

## Executable usage and evidence

- [HTTP identity/admission consumer](../../HttpIdentityDemo/README.md#private-draft-permissions)
  composes late native resource checks, queries and antiforgery-protected versioned edits.
- [CQRS consumer](../../CqrsDemo/README.md) creates and reads drafts using the creator's grants;
  its PostgreSQL cases exercise complete permitted selection and native snapshot timing.
- [A2.1 scope](../../../../docs/plans/a2-1-sales-resource-permissions.md) and
  [implementation report](../../../../docs/reports/a2-1-sales-resource-permissions.md) record
  fresh proofs separately from the A2 assessment and the
  [A2.2 optional RLS report](../../../../docs/reports/a2-2-sales-postgres-rls.md).
