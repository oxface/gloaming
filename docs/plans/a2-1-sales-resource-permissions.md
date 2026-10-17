# A2.1: Sales draft permissions and complete queries

Implementation selected by the owner on 2026-10-16, following the
[A2 assessment](../reports/tenant-authorization-assessment.md): **Sales resource permissions
and complete queries**, with optional PostgreSQL RLS left separate. Prepared on
`feat/sales-resource-authorization` from fetched main `d35b3ee`, reusing the checkout and
carrying forward the existing unstaged assessment. Changes remain unstaged; commit approval
is separate. The user's implementation instruction supersedes the earlier assessment-only
restriction for this bounded capability.

## Consumer behavior

Sales owns positive per-draft Read=1 and Edit=2 grants to canonical human/system subjects,
identified by kind plus opaque key within an Organization. Edit includes read and permits
the existing human edit/submission paths; system grants permit queries, while commands
retain their existing human requirement. Membership administration supplies no draft grant.
No configurable roles, groups, business-derived grants or generic permission engine is added.

The bounded sample creation policy lets a trusted admitted human create a draft and stages
that human's Edit grant in the same native Sales transaction. Migration creates no grants
for existing drafts; historical creator attribution is not guessed. Grant administration,
backfill and accepted grant-change audit are separate consumer capabilities, not public
grant setters introduced here. Finite demo/test setup explicitly provisions its own grants.

`IDraftOrderAuthorization` exposes a native consumer check using the immutable operation
actor/Organization. Existing draft query and command Contracts enforce the same rule at
their actual entry points; the host's earlier boolean check is not sufficient. The private
Sales implementation composes a correlated EXISTS over local grant rows, never a returned
authorized-ID list or cross-module private-table join. Child lines and customer display
fields included in the draft response share this sample's draft-read rule; no independently
protected Personnel/private-field model is invented.

Grant predicates intersect tenant and business predicates before detail loading, count,
totals, ordering and pagination. Overlapping Read/Edit grants cannot duplicate drafts or
inflate totals. Missing/inaccessible resources share NotFound; no grants produce a complete
empty query result at the chosen database snapshot. Missing actor/tenant establishment,
anonymous private access and database errors remain failures, not permissive defaults.

The HTTP host retains native authorization → tenant admission → endpoint placement, with
actor establishment inside native policy authentication as documented by A2. Imperative
native resource handlers run late, then module entry points enforce the decision again.
List/detail and one line-edit route demonstrate consumer adoption; edits retain native
antiforgery, expected-version conflicts and explicit Sales save/commit ownership.

## Timing and boundaries

Access remains the global identity/membership authority and is not joined from Sales.
Fresh human HTTP/job operations admit current membership before publishing tenant context;
already-admitted work retains immutable actor/tenant/initiator. Each resource query reads
current grants under its native statement/transaction snapshot. A new statement can observe
revocation while contexts stay fixed. RepeatableRead deliberately retains an older snapshot;
ReadCommitted count/page can differ after a concurrent grant change. No permission-at-commit
fence, distributed revocation or retry protocol is claimed.

Direct/system callers establish trusted scopes and admit/authorize their actual mandate.
An initiator grants no authority; system and human keys with identical text remain distinct.
RLS, middleware reordering, transport changes, Access/FGA dependencies and public Gloaming
interfaces are excluded. Existing raw/bulk/native persistence bypasses remain consumer-owned.

## Proof obligations and handoff

Use PostgreSQL 18.6 through the existing CQRS/HTTP suites: two same-tenant subjects with
different grants, overlapping grants, sparse pages/exact counts, hidden detail/child totals,
cross-tenant key collisions, read-only mutation/submission denial, creator grant atomicity,
current grant revocation, native snapshot choice, missing/anonymous contexts and failure
propagation. Cover direct/internal/system calls, late native HTTP checks, current membership
admission, immutable admitted work, antiforgery and expected-version conflicts. Upgrade
populated pre-grant storage without invented privilege. Rerun affected rich-aggregate/domain
reaction consumers and architecture checks. Record fresh evidence and remaining gaps in
[the implementation report](../reports/a2-1-sales-resource-permissions.md).

No new reusable mechanism is presumed. This slice can establish useful consumer policy/query
composition without extracting a package or changing template generation. Exports, AI
selection, grant administration, strict revocation timing and performance remain separate
requirements until a concrete consumer selects them.
