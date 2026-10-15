# Access and authorization research intake

Assessment date: 2026-10-14. Baseline: fetched `origin/main` at `824c95b`.
This is a preliminary source assessment and backlog recommendation, not completed
authorization research, engine selection, an ADR, interface approval or runtime proof.
Accept A1 and A2 for later research; prioritize permission/query semantics and data
authority/consistency before choosing an engine. No runtime implementation is authorized.

Current handoff, 2026-10-16: the [A1 + A1.1 assessment](access-authorization-assessment.md)
reconciles this intake against fetched main `7ea6652`, including C1, and provides the
comparative recommendation, models/query examples and focused proof scopes. Its
[released-source evidence](access-authorization-primary-sources.md) supersedes the unpinned
provider observations below where more precise. This intake remains the original dated
record; its “queued,” baseline and link-check statements are historical. A2 has the A1
handoff but remains an assessment follow-up; no runtime authorization/RLS was implemented.

## Current repository evidence

The source review found the following; existing tests were inspected,
not rerun for this assessment. These are current code observations, not new proofs.

- [TenantContext](../../src/Gloaming.Tenancy/Gloaming.Tenancy/TenantContext.cs) is immutable;
  [TenantContextAccessor](../../src/Gloaming.Tenancy/Gloaming.Tenancy/TenantContextAccessor.cs)
  rejects a second initialization. Context establishment does not grant authorization.
- [HTTP composition](../../samples/Wholesale/HttpIdentityDemo/DemoComposition.cs) calls
  `UseAuthorization`, `UseHttpActorContext`, then `UseHttpTenantContext`.
  [Tenant middleware](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/TenantContextMiddleware.cs)
  invokes the consumer resolver before initializing tenant context.
- [OrganizationTenantResolver](../../samples/Wholesale/HttpIdentityDemo/HttpIntegration/OrganizationTenantResolver.cs)
  delegates active-membership admission to
  [ApplicationAccess.ResolveMemberOrganizationAsync](../../samples/Wholesale/modules/Access/Access/ApplicationAccess.cs),
  with an explicit public-Organization exception. This is consumer admission policy.
- [Tenant ownership](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipExtensions.cs)
  creates an equality query filter and ownership concurrency token. Change validation
  checks current and original ownership; consumers must call it from both save overrides.
  This does not establish RLS or protection for raw SQL and bulk operations.
- [Tenancy](../../src/Gloaming.Tenancy/Gloaming.Tenancy/Gloaming.Tenancy.csproj) has no package
  dependencies; [Persistence EF](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/Gloaming.Persistence.EntityFrameworkCore.csproj)
  references EF Relational. Neither establishes an authorization-engine dependency.

## Primary-source findings

**OpenFGA facts.** Modular files and type extensions combine into one model through
`fga.mod`; the mechanism supports ownership of model authoring. Runtime isolation between
application modules is not established by this feature. Dynamic custom roles can be role
objects with user-assignment tuples and permissions linked to assignees.
[Modular models](https://openfga.dev/docs/modeling/modular-models),
[custom roles](https://openfga.dev/docs/modeling/custom-roles).

Contextual tuples last for one request and explicitly support avoiding synchronization.
The documented limit is 100 per request; token-derived memberships can remain effective
until token expiry. OpenFGA recommends application databases for entity hierarchies,
filter/sort/join data and role metadata; some direct permissions or authorization-role
memberships can instead have OpenFGA as their authority.
[Contextual tuples](https://openfga.dev/docs/interacting/contextual-tuples),
[source of truth](https://openfga.dev/docs/best-practices/source-of-truth).

`MINIMIZE_LATENCY` can use caches; `HIGHER_CONSISTENCY` bypasses them and reads the
OpenFGA database. This does not establish atomicity with a consumer database or eliminate
consumer-to-engine propagation delay; that limitation is architectural inference.
[Query consistency](https://openfga.dev/docs/interacting/consistency).

OpenFGA separates database metadata filtering/sorting from authorization and describes
search-then-check, an authorization index plus checks, or accessible IDs followed by search.
Its search guide warns that partial ID sets cannot provide correct global sorting.
Current ListObjects API messages have no page size or continuation token; API comments
describe unsorted results bounded by timeout/result limit. The search guide's pagination
wording therefore must not be treated as proof of resumable ListObjects pagination.
Pin engine/API versions for later verification.
[Search with permissions](https://openfga.dev/docs/interacting/search-with-permissions),
[official API schema](https://github.com/openfga/api/blob/main/openfga/v1/openfga_service.proto).

**Native ASP.NET facts.** `IAuthorizationService` supports imperative resource-specific
authorization; `[Authorize]` alone cannot evaluate an unloaded resource. This establishes
an enforcement baseline, not an automatic permission-aware list-query mechanism.
[Resource authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0).

**PostgreSQL facts.** RLS `USING` filters existing rows; `WITH CHECK` validates proposed
inserted/updated rows and rejects failing writes. Enabled RLS with no applicable policy
denies by default. Permissive policies combine with OR, restrictive policies with AND.
Superusers and `BYPASSRLS` roles bypass RLS; owners normally bypass it unless subject to
`FORCE ROW LEVEL SECURITY`. Referential-integrity checks and table-wide operations have
exceptions, so RLS is not an unrestricted security guarantee.
[CREATE POLICY](https://www.postgresql.org/docs/current/sql-createpolicy.html),
[row security](https://www.postgresql.org/docs/current/ddl-rowsecurity.html).

`set_config(name, value, true)` is transaction-local; `false` sets session state.
`SET LOCAL` ends at commit/rollback and requires a transaction block. Npgsql documents
state leakage when `No Reset On Close` is enabled. Inference: any RLS context proposal
must prove same-transaction/connection use, absent-context behavior and pool reuse.
[Configuration functions](https://www.postgresql.org/docs/current/functions-admin.html#FUNCTIONS-ADMIN-SET),
[SET](https://www.postgresql.org/docs/current/sql-set.html),
[Npgsql settings](https://www.npgsql.org/doc/connection-string-parameters.html).

## Recommended research boundaries and unresolved evidence

A1 should define module-owned permissions, stable application users/external identities,
personnel without login, organization administration without implicit private-data access,
and business-role history separately from authorization roles. Licensing and business-state
invariants remain consumer policy. Prove scoped union grants without widening their scopes.

A1 should compare native handlers plus focused relational permission queries with OpenFGA.
A2 assesses composition with admitted tenancy and an optional PostgreSQL RLS boundary.
Neither an engine nor continual relationship mirroring is the default. Establish authority
for each fact and compare request-local context with any justified persistence, projection
or synchronization proposal.

Required later evidence includes complete authorized filters/sorts/pages/counts/exports/AI
source retrieval; deterministic ordering and duplicate handling; revocation across tokens,
caches and transactions; dependency failure versus denial; and partial-result behavior.
RLS additionally needs owner/bypass-role, view/function, retry, cancellation, background-job
and pooled-connection cases. Evaluate realistic cardinality and query plans before choosing.

Library finding: no new reusable mechanism was proven. Template/sample findings concern
consumer-owned policy and future composition only. No runtime tests were run, no interface
or dependency was added, and no engine-selection or implementation claim follows from intake.

## Intake verification and scheduling

A1 is queued after current DDD/domain-reaction/C1 work, before Access-specific T2 and P2.
A2 baseline research may accompany A1; its composition recommendation follows the authority
and scope decisions. No calendar date or runtime implementation commitment is made.

All 26 local links in the two research briefs and this intake were checked, and git diff
whitespace checks passed. The research branch is based on fetched origin/main 824c95b in
an isolated worktree; the existing checkout/index was not used for edits or staging.
No C# builds, runtime tests, database experiments or deployment checks were run for this
documentation-only intake. Existing test/code links are evidence pointers, not new results.

## Membership suspension follow-up: 2026-10-15

Umbrine requested [A1.1](../plans/a1-access-resource-authorization-research.md#a11-organization-membership-suspension-follow-up)
as a focused addition to Access research. Its priority is unchanged; it is not a separate
pre-development gate or authorization to implement a library/administration API.

**Existing guarantees, inspected rather than newly executed.** The consumer cited Foundry
HEAD f55d803. The selected Access source and AdmissionTests files in research branch baseline
df83518 match that commit. AccessRows already defines Active/Suspended/Removed;
ApplicationAccess admits only active members and resolves global user identity independently.
AccessRegistration/IApplicationAccess expose queries, not administrative lifecycle commands.
OrganizationTenantResolver remains consumer admission policy.

[AdmissionTests](../../samples/Wholesale/HttpIdentityDemo.Tests/AdmissionTests.cs) contains
MissingSuspendedAndRemovedMembershipDenyWithoutPublishingTenant and
RevocationBlocksNextAdmissionWithSameCookieButKeepsGlobalIdentity for both Suspended and
Removed. AlreadyAdmittedOperationRetainsContextWhenMembershipIsRemoved uses Removed
explicitly; the focused extension should cover Suspended too. These tests change database
status directly to exercise admission, not an authorized administrative operation.
The seed already gives one user two active memberships; the combined post-suspension
business read/write/other-Organization scenario remains a follow-up proof target.

**Missing consumer/template behavior.** Assess narrow suspend/reactivate Contracts, target
Organization/membership authorization, explicit repeat/conflict/transition outcomes and a
native concurrency protocol. The inspected membership mapping has no explicit concurrency
revision/token. Assess composing the existing Gloaming audit capability in Access's native
transaction for Organization, target, administrator and previous/new status; no automatic
membership-audit integration currently exists in the selected code. Umbrine owns last-admin,
self-suspension, grant retention and unavailable-capability rules. Identity/Personnel/IdP
lifecycles remain separate.

**Smallest candidate slice.** One consumer Access suspend/reactivate journey with authorized
direct HTTP/non-HTTP entry guidance, native concurrency and atomic business audit. Prove
same-cookie protected read/mutation denial with no denied side effects, usable global
identity/another active Organization, retained admitted-operation context, audited rollback
and reactivation under current permissions with stable identity/history. Actor/tenant
establishment and existing active-membership admission are reused. Engine selection, RLS,
global logout and generalized offboarding/assignment automation are not prerequisites.

**Work ownership decisions.** Distinguish company-owned system work from deferred work
requiring continuing human admission. Decide reminder recipient/content eligibility and
stale-grant/reactivation behavior explicitly. Umbrine starts with module-owned warnings
and manual reassignment of unfulfillable required responsibilities; transition/draft checks
and preserved historical attribution remain module business policy. No automatic cancellation
or reassignment of every workflow is proposed. Future caching must disclose admission
staleness; the inspected per-operation query is the current baseline.

No new reusable mechanism or library gap was proven. This addition records assessment and
future proof scope only. No source, dependency, migration, interface or runtime test changed.
Local links and documentation whitespace were checked; no runtime tests were executed.
