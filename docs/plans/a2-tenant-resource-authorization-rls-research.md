# A2: tenant-aware resource authorization and optional PostgreSQL RLS research

Status: consumer-requested research/backlog item, 2026-10-14. Assessment and documentation
only; no middleware reordering, RLS policies/migrations, packages or public interfaces are
implemented or approved. Related to [A1](a1-access-resource-authorization-research.md), with
[preliminary evidence](../reports/access-authorization-research-intake.md).

## Priority and placement

Schedule in the consumer-driven authorization research block after current DDD/domain-reaction
and C1 work, before Access-specific template adoption and P2 readiness. Existing-code auditing
can accompany A1; finalize authority/order recommendations after A1 defines admitted principals,
resource scopes and permission consistency. Optional RLS implementation is not a prerequisite
for that research and is not selected merely because PostgreSQL is already used.

## A1 assessment input: 2026-10-16

The [A1 + A1.1 recommendation](../reports/access-authorization-assessment.md), prepared
against fetched main `7ea6652`, supplies proposed authority/scopes and complete selection
requirements. Access retains global identity/current human membership authority; business
modules own permission definitions, resource grants and business derivation. Native resource
checks and module-owned relational predicates are the initial recommendation; OpenFGA remains
conditional, with [pinned enumeration/consistency limits](../reports/access-authorization-primary-sources.md).
Cross-module facts need an explicit complete consumer contract or a separately justified
published view/projection; no private DbContext joins or continuous mirroring are assumed.

Use late imperative checks after the current native authorization → actor → admitted tenant
ordering as the first composition. Changing tenant-aware native policy ordering needs its
own scheme/anonymous/failure proofs. Fresh human worker operations admit again; internal
calls in one operation retain immutable context. Company-owned system work and continuing
human authority are distinct policies. A1.1 suspension/reactivation reuses active admission
and adds no RLS dependency or separate gate.

The owner subsequently authorized the [A1.1 consumer journey](a1-1-membership-suspension.md).
Its [new runtime evidence](../reports/a1-1-membership-suspension.md) covers fresh admission,
immutable admitted work, current scoped administration and native transactional audit.
The global admission lookup remains unchanged. These proofs do not complete A2's ordering,
database-role/pooling/RLS research or business-resource authorized-selection obligations.

A2 must assess tenant isolation separately from within-tenant permissions and keep Access's
global pre-admission registry usable without fabricated tenant context. Tenant-only RLS cannot establish
complete permission-aware lists/counts/exports/AI selection, transactional business audit or
cross-store revocation. Preserve the package-free Tenancy core without Access/FGA coupling.
The dated baseline below remains evidence; A2 role/pooling/transaction/retry/bypass research
and proofs are not completed by this handoff. No RLS implementation is authorized.

## Verified baseline, not a new guarantee

Source inspected on fetched origin/main `824c95b`. These are code/documentation findings;
existing tests are historical proof entry points and were not rerun for this documentation task.

| Existing behavior | Evidence and limit |
| --- | --- |
| One immutable operation tenant | [TenantContext](../../src/Gloaming.Tenancy/Gloaming.Tenancy/TenantContext.cs) is immutable. [TenantContextAccessor](../../src/Gloaming.Tenancy/Gloaming.Tenancy/TenantContextAccessor.cs) rejects reads before initialization and a second initialization, including the same value. Deliberate tenantless is distinct from missing context. This establishes selection, not membership/permission. |
| Consumer-owned HTTP admission | [TenantContextMiddleware](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/TenantContextMiddleware.cs) awaits a consumer resolver then initializes the context. [OrganizationTenantResolver](../../samples/Wholesale/HttpIdentityDemo/HttpIntegration/OrganizationTenantResolver.cs) uses active-membership admission for ordinary routes and an explicit public-Organization exception. |
| HTTP tenancy follows native authorization | [DemoComposition.ConfigureHttp](../../samples/Wholesale/HttpIdentityDemo/DemoComposition.cs) calls UseAuthorization, UseHttpActorContext, then UseHttpTenantContext. [NativePipelineTests](../../src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/NativePipelineTests.cs) covers challenge/forbid before resolution. Established tenancy is not available to native authorization handlers in this bounded composition; imperative endpoint resource checks can run later. Do not silently reorder it. |
| EF ownership is filtering and validated tracked writes | [TenantOwnershipExtensions](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipExtensions.cs) configures a context-bound equality query filter and owner concurrency token; ValidateTenantChanges checks current/original ownership. Consumers populate owners, invoke guards in both native save overrides and own transactions. |
| Database/resource enforcement is separate | [Persistence capabilities](../../src/Gloaming.Persistence/docs/capabilities.md) excludes RLS and raw/bulk enforcement. IgnoreQueryFilters, direct SQL, bulk paths and existing tracked entries require separate analysis. Tenant equality does not enforce within-tenant permissions. |
| Independent adoption | [Tenancy core project](../../src/Gloaming.Tenancy/Gloaming.Tenancy/Gloaming.Tenancy.csproj) is package-free. [Persistence project](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/Gloaming.Persistence.EntityFrameworkCore.csproj) depends on EF Relational, not Access, ActorIdentity, Tenancy or OpenFGA. Preserve these boundaries. |

Additional existing proof entry points: [operation accessor tests](../../src/Gloaming.Tenancy/tests/TenantTests/AccessorTests.cs),
[ownership validation tests](../../src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/OwnershipTests.cs)
and [native PostgreSQL ownership tests](../../src/Gloaming.Persistence/tests/PersistenceTests/GuidOwnershipTests.cs).
Their presence does not establish RLS or tenant-aware authorization ordering proofs.

## Research questions

1. **Admission and propagation.** Trace an admitted Organization through resource checks, lists,
   exports, workers and internal module calls. Distinguish selected tenant, fresh membership
   admission, actor authentication and resource permission. Define fresh scopes, explicit system
   actors and trusted contracts at non-HTTP boundaries; no ambient tenant switching or implicit
   propagation into a newly created scope. Decide whether queued work rechecks current admission
   or carries a consumer-approved decision with an explicit revocation contract.
2. **Authorization order.** Compare a late imperative resource check after existing admission
   with a separately reviewed tenant-aware policy integration. Cover native scheme selection,
   actor mapping, named/default/fallback policies, challenge/forbid and AllowAnonymous/tenantless
   routes. An authorization handler cannot simply assume the current adapter has already
   established a tenant. Tenant selection must not itself authorize access.
3. **Isolation versus permission.** Separate cross-tenant row isolation from within-tenant
   visibility predicates. An authorized Organization member may still lack access to every
   project or private Personnel record. Pair detail checks with correct list/count/export/AI
   query semantics from A1; do not treat RLS tenant equality as an FGA replacement.
4. **Optional database integration.** Evaluate a narrow PostgreSQL-only opt-in: policy/table
   ownership, USING for row visibility/targeting and WITH CHECK for inserted/updated values.
   Cover default-deny when enabled without an applicable policy, permissive/restrictive policy
   combination, schema additions, same-tenant relationships and intentional global tables.
   Do not introduce provider dialect abstractions or a mandatory provider package in the core.
5. **Roles and threat model.** Examine non-owner application roles, BYPASSRLS/superuser behavior,
   FORCE ROW LEVEL SECURITY, migration/admin roles, view/function execution context, privileges,
   referential-integrity exceptions and table-wide operations. State what a runtime credential
   holder can change. A tenant setting supplied through a mutable session variable does not
   authenticate the Organization; do not advertise protection from a caller able to forge that
   setting, privileged roles or every SQL-injection/information-leak path.
6. **Transaction/connection lifecycle.** Assess transaction-local context on the exact native
   connection/transaction before any protected query/write. Cover lazy connection opening,
   pooled DbContexts and physical connection pools, success/rollback, cancellation, transient
   retries and reconnects. Each retried transaction must establish admitted context again; a
   session-global setting or reliance on pool reset alone is not a proven isolation protocol.
   Decide whether otherwise nontransactional reads must acquire an explicit local transaction.
7. **Missing and tenantless context.** Define absent/empty/malformed values and deliberate
   tenantless operations separately. Protected tenant data must not gain a permissive default
   when context is missing. Global tables and maintenance use explicit, independently authorized
   paths; do not make tenantless equivalent to all tenants.
8. **Bypasses and failures.** Assess EF filters disabled, tracked/identity-map reads, raw SQL,
   ExecuteUpdate/Delete, bulk tools, imports/exports and separate connections. RLS, when effective
   for the executing role, covers a different database boundary from EF tracking guards; it does
   not supply aggregate invariants, event/audit obligations or business concurrency. Describe
   dependency outages, missing transaction/context and denied/failed outcomes precisely.

## Required future proof matrix

These are candidate executable follow-ups, not tests added or executed by this research intake.

| Case | Evidence needed before support |
| --- | --- |
| Alpha/beta reads and inserts/updates/deletes | Registered policies under the actual application role, USING/WITH CHECK coverage and explicit cross-tenant failures; native EF plus direct SQL paths. |
| Same tenant, different private-resource grants | Denied within-tenant resources remain hidden through checks, queries, counts, exports and AI selection even when RLS permits the tenant row. |
| Pooled connection reuse alpha → beta → missing | No prior tenant leaks after success, rollback, cancellation, connection reset/reconnect or a new operation lease. |
| Concurrent operations | Separate contexts/connections do not exchange tenant values; reject shared mutable tenant/context usage rather than claiming EF contexts support parallel work. |
| Retry after transient failure | Fresh native transaction/context establishment before the retry's first protected operation; no swallowed setup failure. |
| Missing, empty, malformed or tenantless | Defined denial/error for tenant tables; only explicitly authorized global operations remain possible. |
| Owner/admin/BYPASSRLS and policy omitted/disabled | Actual bypass boundaries are demonstrated and deployment privileges verified; no library-only configuration guarantee inferred. |
| Raw/bulk operations and constraint failures | Document database protection versus bypassed EF/business protocols, SQL errors and transaction rollback. |
| HTTP and non-HTTP ordering | Native challenge/forbid, named schemes/policies, anonymous/public exceptions, tenant-aware resource checks and worker/internal entry checks use the intended admitted context. |
| Concurrent membership/grant revocation | A1's chosen check/commit/export timing remains explicit; RLS tenant isolation cannot silently claim immediate resource revocation. |

## Deliverables and decision gate

Return a code-backed baseline, primary-source/version-backed protection matrix, recommended
composition options, threat model/operational costs, unresolved choices and bounded follow-up
proofs. Cite official PostgreSQL, Npgsql and ASP.NET sources; distinguish provider facts from
application policy and proposed integrations. Recommend no new package if explicit consumer
setup suffices. Any reusable mechanism needs independent-adoption evidence and owner interface
review. Keep Tenancy core free of mandatory EF, OpenFGA and application Access dependencies.
