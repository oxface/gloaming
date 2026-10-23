# E3.3 persisted Access lookup and Organization admission

2026-10-05. The owner approved [the plan and admission policies](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-3-persisted-access.md).
Implementation, consumer usage and proofs were owner-reviewed and checkpointed as `20a02be`.
E3.2 was checkpointed as `cfbac9a`; its checkpoint documentation updates accompanied E3.3.

## Outcome and ownership

The HTTP sample now uses Access-owned PostgreSQL tables and native migrations for global
users, exact external-identity pairs, canonical Organizations and current membership.
Configuration directories are removed. Identity mapping resolves existing provider links
without account creation/profile updates; protected Organization admission queries active
membership before publishing tenant context. One user can enter multiple Organizations.

The public catalog explicitly opts into public Organization lookup for anonymous callers
and mapped authenticated non-members. Native AllowAnonymous does not itself bypass member
admission. Unknown/inaccessible Organizations share the existing generic 404 response;
unmapped authenticated actors still fail with 403 on public endpoints. Database faults
propagate to native 500 handling, never anonymous/tenantless/public fallback.

Suspension/removal committed before the next protected admission blocks that operation with
the same valid cookie. Already-admitted operations retain their original contexts; there is
no revocation guarantee through a later mutation commit. [ADR 0003](../adr/0003-access-registry-and-operation-admission.md)
records the approved registry and operation-admission direction.

**No new reusable mechanism was extracted or proven.** Existing asynchronous actor mapping,
candidate selection and admission/publication seams are reusable with this real data source
without modifying a technical library. Access's domain read Contract, registry model,
public exception, error presentation and admission policy remain consumer-owned.

## Fresh execution evidence

| Suite | Passed | Evidence |
| --- | ---: | --- |
| HttpIdentityDemo.Tests | 49 | Real PostgreSQL 18.6 Testcontainers, native migrations/TestServer/protected cookies, persisted identities and admission, revocation, public non-member access, constraints, faults/cancellation, finite setup child process. |
| ActorIdentityTests | 19 | Existing core identity/lifecycle behavior. |
| TenantTests | 17 | Existing independent tenancy core. |
| ContextDemo.Tests | 15 | Existing independent/combined operation composition. |
| EntityFrameworkCoreTests | 23 | Existing supported ownership model and write validation. |
| ActorIdentityAspNetCoreTests | 15 | Existing actor-only HTTP adoption/policy behavior. |
| TenancyAspNetCoreTests | 39 | Existing tenancy-only HTTP adoption and selection/admission behavior. |
| ArchitectureTests | 35 | Existing dependency declarations/compiled rules and Inventory/Sales model policies. |
| Total freshly run | **212** | 163 container-free cases plus 49 PostgreSQL HTTP/Access cases; no skips/failures in final runs. |

The sample suite preserves its original 17 useful composition cases against persisted
witnesses and adds 32 cases. Real issuer pairs sharing an email claim still map to different
global users; explicitly pre-linked accounts map to one user. Missing/suspended/removed
membership publishes no tenant. Revocation uses a separate database operation and reuses
the exact native cookie. Public non-members remain identified, and route/subdomain modes
share persisted admission. HTTP results expose independently expected tenant identities and
fixture quantities 42/7; these are not Inventory database isolation results.

Nine constraint cases exercise the actual consumer migration, then assert that admitted
identity remains correct after each rejected write. They protect issuer-pair uniqueness,
canonical slug uniqueness/validation, current-membership uniqueness, three required
relationships and invalid status values. Removed history coexists with one current
membership. No generic EF/Npgsql behavior matrix or architecture parser was added.

A real table rename exposes admission failure as 500 with no publication/downstream work;
repair permits a fresh request. A native challenge succeeds while Organization lookup is
unavailable, proving admission did not run. Cancellation occurs while the actual member
query waits behind a PostgreSQL table lock; it publishes nothing, reaches no downstream
work, and a fresh request succeeds after lock release. An admitted-operation barrier proves
that later removal does not rebind that operation's actor/tenant.

The first cancellation proof timed out because its activity observer shared the lock
transaction's cached statistics snapshot. Autocommit observation fixed the test without an
application change. PostgreSQL documents [transaction-scoped activity snapshots](https://www.postgresql.org/docs/18/monitoring-stats.html#MONITORING-STATS-VIEWS).
The first complete sample run passed 48/49; the corrected complete run passed all 49.

The finite child process applies the consumer migration and demonstration seed without
OIDC settings, exits without starting HTTP, and its persisted rows are then consumed by the
native HTTP test host. A separate case proves ordinary host startup does not migrate or
seed an empty database, and returns 500 for unavailable catalog lookup while health remains
the explicitly documented liveness response. Neither proof contacts an OIDC provider.

The 18-project active solution builds with zero warnings/errors. Active semantic style and
analyzer verification pass. The runtime explicitly references the existing pinned EF Core
Relational version: design-time PrivateAssets alone otherwise left the test consumer using
Npgsql's older EF minimum. No package version or library dependency changed.
Repository CSharpier verification passes for **148 files**; archive checksum verification
passes for all **800 original files**. Whitespace and local Markdown link checks pass.
The existing finite ContextDemo runs successfully with no Access/database dependency.

No E2 PostgreSQL, archived behavior, broker, real-provider, Kestrel or Aspire topology suite
was rerun in this slice. Their historical results do not prove this new ingress.

The checkpoint hooks subsequently passed formatting, style/analyzers and commitlint, plus
163 active container-free tests and 21 archived architecture tests (184 total, no skips or
failures). The PostgreSQL suite does not run in hooks; its 49-case result above is the
implementation verification, not a checkpoint rerun.

## Library, template and sample findings

**Library:** both cores and both HTTP adapters remain unchanged and independently adoptable.
Their existing asynchronous resolution interfaces accommodate native EF reads and failed
admission without a new persistence/actor bridge, evaluator or transaction middleware.

**Template:** the executable sample supplies editable Access registration/read Contracts,
explicit public metadata, native schema/history/design-time factory, finite setup and global
failure handling. Materialized template/bootstrap CLI output remains E10. Sample HTTP tests
now run in the PostgreSQL CI lane, outside container-free hooks; no fixture data adapter is
retained just to keep that lane fast. Standalone HTTP adapter suites remain container-free.

**Sample:** Access is a global registry, using explicit user/Organization keys before tenant
establishment; it does not enroll these tables in E2 filters and then bypass them. Native
request-scoped contexts read sequentially, never save or retry requests. Setup owns native
migration and a seed transaction/save/commit explicitly. Inventory retains its guarded fixture
catalog and Contract. Folders establish ownership within one host, not assembly isolation.

## Review-worthy files and remaining gaps

- [Access Contract](../../samples/Wholesale/modules/Access/Access.Contracts/IApplicationAccess.cs)
  and its user/Organization/external-identity value types; [actual reads](../../samples/Wholesale/modules/Access/Access/ApplicationAccess.cs).
- [Actor mapping](../../samples/Wholesale/Api/HttpIntegration/ApplicationActorResolver.cs),
  [tenant admission](../../samples/Wholesale/Api/HttpIntegration/OrganizationTenantResolver.cs),
  [public exception](../../samples/Wholesale/Api/HttpIntegration/OrganizationAccessExtensions.cs)
  and [consumer composition](../../samples/Wholesale/Api/DemoComposition.cs).
- [DbContext/model](../../samples/Wholesale/modules/Access/Access/Persistence/AccessDbContext.cs),
  [native migration](../../samples/Wholesale/modules/Access/Access/Persistence/Migrations/20261005204257_InitialAccess.cs),
  [finite setup entry point](../../samples/Wholesale/Api/Program.cs) and
  [staged demonstration rows](../../samples/Wholesale/modules/Access/Access/AccessDemoSeed.cs).
- [HTTP/admission proofs](../../samples/Wholesale/IntegrationTests/Api/AdmissionTests.cs),
  [database/setup proofs](../../samples/Wholesale/IntegrationTests/Api/PersistenceTests.cs)
  and the [updated original composition proofs](../../samples/Wholesale/IntegrationTests/Api/CompositionTests.cs).

Setup supports fresh disposable demonstration databases, not repeated seed reconciliation
or account provisioning. The public marker is an endpoint-level sample exception, not a
general membership/permission policy engine. Trusted non-HTTP callers explicitly perform
admission and tenant initialization; possessing a UserId/OrganizationId alone grants no authority.

Persisted Inventory/business ingress and module project structure are proposed in
[E3.4](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-4-persisted-business-ingress.md). Account
creation/linking, profiles, invitations, membership administration, roles/permissions,
user/Organization disabling, session invalidation, real OIDC/proxy topology, antiforgery
mutations, tenant-aware native authorization handlers and shared transactions remain separate
increments. E3 is not complete.

Source links now follow the E3.4 module/host relocation. Ownership, counts and fixture-catalog
limits above describe checkpoint `20a02be`; [E3.4](e3-4-persisted-business-ingress.md) records
the subsequent persisted business integration rather than rewriting E3.3 evidence.
