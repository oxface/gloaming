# E3.3 persisted Access lookup and Organization admission

Status: owner-approved scope and admission policies, 2026-10-05. Implementation was
owner-reviewed and checkpointed as `20a02be`. E3.2 was checkpointed as `cfbac9a`.
[The execution report](../reports/e3-3-persisted-access.md) records fresh results separately
from the planned obligations below. No new library interface is introduced.

## Outcome and scope

Replace the HTTP sample's configured identity and Organization directories with an
Access-owned PostgreSQL model. A native authenticated principal resolves to a stable
application user; the selected Organization is admitted before tenant publication.
Protected Organization operations require current active membership. Public catalog reads
use an explicit public-access policy, independently of the caller's authentication state.

Deliver one lookup/admission capability, with native migrations, explicit disposable-demo
setup, executable HTTP usage and real PostgreSQL proofs. Keep the existing Inventory catalog
fixture for this increment. Persisted business data and mutations using E2 ownership utilities
follow in [the proposed E3.4 read slice](e3-4-persisted-business-ingress.md) and subsequent
mutation work; this increment must not claim database isolation of Inventory data.

No changes to the actor/tenancy cores or HTTP adapters are expected. Access is consumer-owned
sample/template code. No new reusable mechanism has been proven by preparing this plan.

## Evidence and deliberate changes

The archived [external-identity model](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access/Persistence/ExternalIdentityRecordConfiguration.cs)
uniquely keys identities by issuer and subject and relates them to global users. The
[linking handler](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access/Identity/LinkExternalIdentityHandler.cs)
also automatically creates users, updates profiles and resolves concurrent linking. Retain
the identity key and global user relationship; defer those write workflows. Do not link
accounts by email or create a user during ordinary request mapping.

Archived [Organization queries](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access/Organizations/Queries/OrganizationQueries.cs)
admit active members using an explicitly selected slug, before Organization scope exists.
They bypass a scope filter and build a combined identity/membership/role context. Retain
current-membership admission and multiple Organizations per user; replace filter bypass
and the combined context with explicit Access queries and the independent existing contexts.
Roles are not needed to prove membership admission.

The archive's [membership statuses](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access.Contracts/Organizations/MembershipStatus.cs)
and [configuration](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access/Organizations/Persistence/MembershipConfiguration.cs)
distinguish active/suspended membership from ended membership, preserving removed history.
Use that minimal distinction without importing invitations, role assignments, last-admin
rules or membership-management endpoints.

These are source findings, not freshly executed archive tests. E3.2's fixture behavior and
E2's storage results remain historical evidence until the corresponding new proofs run.

## Owner-approved admission policies

1. **Fresh admission per operation:** query current membership for each protected request.
   Removal or suspension committed before the next admission query blocks that request,
   including when it presents the same still-valid cookie. An already-admitted operation
   keeps its immutable actor/tenant context; revocation does not retroactively cancel it.
   A concurrent admission query can observe the state before revocation commits. This is
   an admission-time guarantee, not serializable authorization through business commit.
2. **Explicit public access:** the catalog is public for anonymous callers and authenticated
   non-members alike. A mapped authenticated actor remains identified. This exception grants
   access only to the public capability; protected Organization operations still require
   active membership. An authenticated principal that cannot map still fails actor mapping,
   including on an otherwise public endpoint.

Use consumer-owned endpoint metadata, proposed as `PublicOrganizationAccessAttribute` and
`.AllowPublicOrganizationAccess()`, to select public lookup. Member admission is the default
for selected Organizations. `.AllowAnonymous()` only changes native authentication policy;
it does not implicitly disable membership admission. `.AllowTenantless()` only permits an
explicit absence of tenant selection. These mechanisms remain separate.

| Endpoint | Native authentication | Tenant requirement | Access admission |
| --- | --- | --- | --- |
| `/organizations/{organization}/catalog` | AllowAnonymous | Required | Explicit public Organization access |
| `/organizations/{organization}/identity` | RequireAuthorization | Required | Active membership |
| `/identity` | RequireAuthorization | Tenantless allowed | Global user mapping; no selected Organization |
| `/public-identity` | AllowAnonymous | Tenantless allowed | Map authenticated callers; explicit anonymity otherwise |
| `/health`, `/login` | AllowAnonymous | Tenantless allowed | No selected Organization |

Only human application actors are admitted to member operations in this first consumer.
System-actor authority needs a separate explicit product policy. Known-but-inaccessible and
unknown Organizations both return the existing generic 404 ProblemDetails response. Missing
selection under a required endpoint remains 400; unmapped authenticated identity remains
403; native challenges/forbids retain their behavior. Database faults propagate to the
native 500 handler rather than becoming a mapping denial or tenantless result.

## Consumer interface and ownership

Propose a small `Access/Contracts/IApplicationAccess` interface with three asynchronous reads:

```csharp
Task<UserId?> ResolveUserAsync(
    ExternalIdentity identity, CancellationToken cancellationToken);
Task<OrganizationId?> ResolvePublicOrganizationAsync(
    string candidate, CancellationToken cancellationToken);
Task<OrganizationId?> ResolveMemberOrganizationAsync(
    UserId user, string candidate, CancellationToken cancellationToken);
```

The sketch uses consumer-owned `UserId`, `OrganizationId` and `ExternalIdentity` value types.
Their values are validated and explicit; these are not technical-library identities.
Keep the sample's existing opaque user/Organization keys (`application-alpha`,
`wholesale-alpha`, etc.) so persistence adoption does not silently change actor/tenant
meaning. Issuer and subject use exact comparison without trimming or case conversion.
The interface reads canonical identities and admission, not profiles or role collections.
It needs no EF, HTTP, ActorIdentity or Tenancy types in its contract.

Access's internal EF implementation owns these reads. `ResolveMemberOrganizationAsync`
queries the selected Organization and active membership together in one database statement.
Neither caller-controlled user IDs nor claims are treated as authorization proof: the HTTP
resolver obtains UserId from the established human actor, and ordinary non-HTTP consumers
must establish a trusted actor and explicitly invoke admission before initializing tenancy.
Neither the context nor this read interface is a universal permission system.

The host's actor resolver reads the one validated issuer/subject pair using the existing
claim-shape policy, calls `ResolveUserAsync` and explicitly maps UserId to ActorId. It writes
no profile and saves nothing. Unknown identities return a mapping failure, never anonymous.

The host's Organization resolver receives the route/subdomain candidate through the existing
library preset. Absence yields deliberate tenantless execution. Otherwise it selects public
or member lookup from endpoint metadata, then maps admitted OrganizationId to TenantId.
Unknown/denied results return null; no tenant is published. The consumer composes the actor
and tenancy libraries; neither library gains a dependency on the other or on Access.

Keep implementation and Contracts under the existing host's `Access/` folder for this
bounded increment, with explicit composition and internal implementation types. Move the
existing root identity fixture/resolver responsibilities into Access and remove the two
configuration directories. This establishes ownership, not assembly-level module isolation.
Review actual module projects and the state-stored business Contract together in E3.4,
rather than adding empty module projects or moving unrelated proofs now.

## Persistence and initialization

`AccessDbContext` owns schema `access` and migration history `access.__EFMigrationsHistory`.
Use the existing pinned EF/Npgsql dependencies and native EF migration scaffolding. It has
no current-tenant dependency: global identity lookup and admission must work before tenancy
is initialized. This is a narrow consumer-owned registry, not an unrestricted business-data
context; every member query names both the selected Organization and the trusted user.
Do not attach E2 tenant filters just to bypass them for these reads.

| Table | Required product invariants |
| --- | --- |
| `users` | Stable global user key; no email identity key or speculative profile/status model |
| `external_identities` | Exact unique `(issuer, subject)`; required FK to user; multiple identities may reference one user |
| `organizations` | Stable Organization key; unique canonical slug |
| `memberships` | Membership key, required user/Organization FKs, explicit status; at most one current membership per pair |

Membership statuses are `Active = 1`, `Suspended = 2`, `Removed = 3`. Persist explicit values
with a database check rejecting 0 and undefined values. A PostgreSQL partial unique index
over `(organization_id, user_id)` for Active/Suspended permits removed history while
preventing conflicting current memberships. Only Active admits member access. This does
not introduce status-transition commands or promise reinvitation behavior.

For route/subdomain parity, store lowercase ASCII DNS-compatible slugs of 1–63 characters,
with alphanumeric ends and alphanumeric/hyphen content. Accept ASCII case differences in
lookup by canonical lowercasing; reject whitespace and punctuation rather than rewriting
them into a different slug. Slug lookup policy belongs to Access; library candidates and
canonical identity keys retain their existing exact-preservation behavior.

Provide an explicitly invoked finite `--initialize-access` demo mode and a native design-time
factory. The mode uses a caller-supplied disposable connection, calls `Database.MigrateAsync`,
then stages reviewed demo rows and explicitly saves them. It exits without starting HTTP.
Normal host startup neither migrates nor seeds. A fresh database is the supported demo-setup
target; reset/provisioning/repeated seed reconciliation are not general product workflows.
Keep connection configuration separate from native OIDC settings so database setup needs no
identity provider. Do not retain a fixture-directory production fallback.

Request-scoped DbContexts are read-only for this journey, used sequentially during actor
mapping then admission, and disposed by native DI. Pass RequestAborted to each read.
Unexpected database faults abort the request; no implicit retry, save, transaction or scope
rebinding occurs. Setup explicitly owns its transaction/save/disposal; no transaction spans
Access admission and a later business operation.

## Executable proofs and test placement

Use the existing real PostgreSQL Testcontainers fixture and native TestServer/protected
cookies. Migrate each disposable test database through the actual consumer migration;
seed known user, identity, Organization and membership rows explicitly. Cookie creation is
test-only and proves mapping of a trusted native principal, not remote OIDC validation.
No production header-authentication scheme or fake data-backed query implementation.

Adapt `HttpIdentityDemo.Tests` to this real database consumer. Move its execution from the
container-free CI lane/hooks to the active PostgreSQL lane; keep the independently adoptable
actor/tenancy HTTP suites container-free. Preserve meaningful existing composition cases,
replacing directory configuration with persisted witnesses rather than keeping a second
fixture host solely for hook execution. Test sample behavior through HTTP and Access
Contracts; SQL may prepare revocation, invalid rows and fault conditions.

| Proof | Observable evidence |
| --- | --- |
| Global identity | Same subject at two issuers maps to different users; pre-linked external identities can map to the same user. Email claims do not link users. Unknown/ambiguous principal fails before business work. |
| Multiple Organizations | Same actor/cookie reaches Alpha and Beta with active memberships; canonical tenant differs and actor is unchanged. |
| Admission denial | A user active only in Beta cannot reach protected Alpha; missing, suspended and removed membership deny. Unknown and inaccessible Organizations have the same generic response; tenant remains unpublished. |
| Native ordering | Anonymous protected requests retain native challenge and never begin tenant admission; protected successful requests use the established application actor. |
| Revocation | Commit removal/suspension in a separate setup scope, reuse the same cookie on the next request, observe denial; global identity remains usable. An operation admitted before the commit retains its original context. |
| Public exception | Anonymous callers and mapped authenticated non-members can read the public catalog; protected access remains denied and authenticated public attribution remains human. |
| Selection parity | Route and subdomain presets use the same persisted canonical Organization/admission rules, including host case handling. No automatic fallback. |
| Relational invariants | Duplicate external pair/current membership and dangling ownership FKs are rejected by the actual migration; invalid status cannot become valid membership. Test application invariants, not a general EF/Npgsql test matrix. |
| Failure recovery | A real Access lookup fault publishes no tenant and invokes no downstream business work; a later fresh request after repair succeeds. Cancellation never falls back to public/tenantless access. |
| Setup and adoption | Finite setup creates the Access schema/history and exits; native host reads its rows. No libraries acquire Access dependencies; existing standalone suites remain valid. |

The admitted-operation/revocation case can use a test-host barrier after tenant establishment,
without adding a timing hook to production code. It proves the proposed operation lifetime,
not revocation safety through a future mutation transaction. Avoid reimplementing HTTP
adapter fault matrices or asserting native framework internals already covered elsewhere.

Run the adapted sample suite on PostgreSQL, independent HTTP/context suites and existing
architecture checks, active build/style/analyzers, formatting and archive checks. Run the
existing PostgreSQL suites if integration changes their consumers. Add precise commands and
new CI placement to the implementation handoff; report actual fresh counts without inheriting
E2/E3 results. No real-provider login, broker or Aspire result is claimed.

## Library, template and sample findings to report

**Library:** exercise existing asynchronous actor resolution and candidate/admission seams
with real data. Propose an adapter change only if a concrete integration gap appears and
review it separately. No new reusable mechanism is currently justified.

**Template:** deliver editable Access lookup/admission, explicit public exception, native
registration, schema/history, finite setup and failure-response recipes in the runnable
sample. Consumer policies remain visible. Materialized template/CLI output stays in E10.

**Sample:** Access owns global user/provider links and Organization membership admission;
Inventory retains its guarded fixture catalog and existing Contract. Fixture reads do not
prove tenant-discriminated business persistence or full project/module isolation.

Defer account provisioning/linking, profiles, invitations, status administration, roles and
permissions, user/Organization disabling, session invalidation, antiforgery mutations,
real-provider/AppHost topology, tenant-aware native authorization handlers and shared
transactions. E3.4 should connect this admitted HTTP operation to a real E2 state-stored
business capability and review the module project structure it requires. E3 remains open.

Leave implementation changes unstaged for owner review. The approved registry and
operation-admission direction is recorded in [ADR 0003](../adr/0003-access-registry-and-operation-admission.md);
its implementation was owner-reviewed in checkpoint `20a02be`.
