# Wholesale API

The [Wholesale product](../README.md) owns this single HTTP host. Its default composition also
exposes `POST /organizations/{organization}/draft-orders`,
`PUT /organizations/{organization}/draft-orders/{draftId}/lines` and
`POST /organizations/{organization}/draft-orders/{draftId}/submit`. Creation accepts the native
`CreateDraftOrder` Contract. Batch line editing accepts `expectedVersion`, `add` (complete line
DTOs), `change` (the existing line-edit DTOs) and `remove` (line IDs). Submission accepts
`expectedVersion`. All writes use authenticated, admitted Human context and actor-bound
antiforgery. Existing Edit grants govern line changes/submission; creating a draft grants Edit
to its creator. Native module validation and required allowance reservation run before commit.

Finite full product setup applies Purchasing migrations too and seeds EUR 1000 customer
allowances. Exceeding an allowance returns Conflict with the draft/allowance rolled back.
No allowance administration API is supplied. The optional restricted Sales-RLS mode intentionally
keeps existing-draft/profile operations and omits create/submit, preserving its limited grants;
it has no blanket authority to create resource grants or update allowances.


A runnable consumer composing the optional [actor](../../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/README.md)
and [tenancy](../../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/README.md) adapters with native
cookie/OIDC authentication and separate PostgreSQL-backed Access/Inventory/Sales modules.
Business reads and profile edits use module Contracts and tenant-owned rows.

[Private draft permissions](#private-draft-permissions) add Sales-owned resource grants,
complete permitted lists/counts and late native HTTP resource checks.

[Membership administration](#membership-administration) now supplies the consumer-owned
suspend/reactivate journey with current scoped authority, native concurrency and atomic audit.

Accepted Sales profile edits now explicitly stage a [transactional audit](../../../src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/README.md)
in the same native transaction as the customer/version and address changes. Sales requires an
established Human actor, retains canonical actor/tenant and the affected address/version transition,
and deliberately excludes name/address text. The module's new forward migration creates
`sales.audit_entries`; apply finite setup/migrations before using this capability. Failures,
conflicts, admission/antiforgery rejection and cancellation produce no accepted-change audit.
Direct non-HTTP callers explicitly establish their trusted actor as well as tenant and own
authorization. Profile reads do not require actor-context establishment. Audit completeness,
classification, visibility and retention remain Sales policy; no denial auditing is introduced.
The internal SalesAudit wrapper captures established actor/tenant, clock and generated entry
ID. Its named ProfileChangedDetailsV1 record keeps the payload fields and version constant
together; business code supplies only the profile/version change. Audit stores no correlation,
causation or trace IDs; native HTTP telemetry remains separate. Each owning DbContext selects JSONB through native EF mapping; native module
mapping owns tenant visibility and
the opt-in HasSubjectTimelineIndex helper and its native index customization.

For local orchestration, use the [active Aspire runtime guide](../AppHost/README.md).
The standalone commands below remain supported. [ServiceDefaults](../ServiceDefaults/README.md)
is explicitly registered by the HTTP branch; finite setup starts no host or exporters.

## Configure and initialize

Use normal .NET configuration, with user secrets or your usual secret provider for credentials.
Environment variables use double underscores instead of colons.

| Key | Meaning |
| --- | --- |
| `ConnectionStrings:Access` | Access/Inventory runtime connection; also Sales when its optional separate connection is absent. Finite demo initialization uses this connection as migration authority. |
| `ConnectionStrings:Sales` | Optional separate restricted Sales runtime connection; selects automatic transaction-local RLS for profile/draft HTTP operations. |
| `ConnectionStrings:SalesMigrations` | Sales table owner, used only by `--initialize-sales-rls`. Keep this credential out of normal HTTP configuration. |
| `ConnectionStrings:Setup` | Administrative provisioning authority, used only by the optional Aspire `--initialize-rls-demo` mode. Never pass it to normal HTTP. |
| `RuntimePasswords:Access`, `RuntimePasswords:Sales` | Optional separate secret passwords for password-free runtime connection strings. Program combines them through Npgsql before setup/service registration. |
| `Oidc:Authority` | HTTPS OIDC provider authority for HTTP login. |
| `Oidc:ClientId` | Registered authorization-code client. |
| `Oidc:ClientSecret` | Credential when that client registration requires one. |
| `DemoIdentity:Issuer`, `DemoIdentity:AlphaSubject`, `DemoIdentity:BetaSubject` | Optional finite-setup mapping inputs. Supply all three together, with HTTPS issuer and distinct subjects. |

Set `ConnectionStrings__Access` to a disposable, fresh database and run the finite setup:

```bash
dotnet run --project samples/Wholesale/Api/Wholesale.Api.csproj -- --initialize-demo
```

This mode needs no OIDC configuration. It explicitly migrates `access`, `inventory` and `sales`, each
with its own `__EFMigrationsHistory`, then stages demonstration rows and saves/commits native
transactions. Inventory/Sales setup establishes a separate tenant context for each Organization.
It exits without starting HTTP. Normal HTTP startup neither migrates nor seeds.
`--initialize-access` remains an Access-only finite mode; it supplies no Inventory/Sales data.
Setup makes no atomic cross-module transaction guarantee.
Repeated seed reconciliation is unsupported; rerunning setup against populated demo rows
fails instead of rewriting membership. The database is consumer-owned and is not dropped.

The seed contains two global users: `application-alpha` belongs to `wholesale-alpha`
(`north-supply`) and `wholesale-beta` (`south-supply`); `application-beta` belongs only to Beta.
Its external accounts are fictional exact pairs `https://identity.test`/`shared-subject`
and `https://other-identity.test`/`shared-subject`. For actual login, provision your validated
issuer/subject pair against an application user explicitly; neither request mapping nor
setup automatically provisions provider accounts, creates users or links by email. The
optional local Keycloak graph passes its real issuer/subjects to finite setup explicitly.
Partial, non-HTTPS or duplicate-subject inputs fail before migrations/data setup.

The [design-time factory](../modules/Access/Access/Persistence/AccessDesignTimeFactory.cs) supports native EF tooling:

```bash
dotnet ef database update --project samples/Wholesale/modules/Access/Access/Access.csproj
dotnet ef database update --project samples/Wholesale/modules/Inventory/Inventory/Inventory.csproj
dotnet ef database update --project samples/Wholesale/modules/Sales/Sales/Sales.csproj
```

This applies migrations only. Supply the connection through `ConnectionStrings__Access`.
Native migration scaffolding remains ordinary reviewed consumer C#.

## Run HTTP

Configure OIDC and local HTTPS using native ASP.NET Core certificate setup. Register the
actual `/signin-oidc` callback with your provider, then run:

```bash
dotnet run --project samples/Wholesale/Api/Wholesale.Api.csproj -- --urls https://localhost:7443
```

Visit `/login` to initiate native OIDC login and return to `/identity`. Without logging in,
`/organizations/north-supply/catalog` and `/organizations/south-supply/catalog` return 42 and 7
available `DEMO-NOTEBOOK` units from their persisted Inventory rows. Use `?sku=YOUR-SKU`
to select another exact SKU; an absent SKU returns 404, a blank selection returns 400.
The protected `/organizations/{organization}/stock/{sku}` endpoint requires native
authentication and current active membership before reading the same isolated catalog.
The default challenge uses native cookies, so recognized JSON API endpoints return 401
without sending the browser to the provider. `/login` explicitly challenges the named OIDC
scheme. Native token validation, HTTPS metadata and code-flow/PKCE remain unchanged.

## Membership administration

The [Access consumer guide](../modules/Access/README.md) records the selected policies,
transaction protocol, setup and limits. Apply the forward Access migration before these
endpoints are used. Existing memberships get revision 1 and no administrative grant;
migration does not bootstrap privileges. Explicit finite demo setup gives Alpha the North
administration grant and Beta the South administration grant, without expanding memberships.

For a target membership ID known to the administrative client:

| Request | Behavior |
| --- | --- |
| GET `/organizations/{organization}/memberships/{membershipId}` | Authorized current status, revision and narrow admin-grant read; unauthorized/foreign/missing gives 404. |
| POST `.../memberships/{membershipId}/suspend` | Suspend an Active membership using JSON `{ "expectedRevision": 1 }`. |
| POST `.../memberships/{membershipId}/reactivate` | Reactivate that same Suspended membership using its current revision. |

POST requires the same native authentication cookie, antiforgery cookie and X-CSRF-TOKEN
flow as profile edits. Both mutation endpoints return the current membership on 200:
a changed revision identifies an accepted transition, while an equal-state request with
the current revision is a no-op. Stale revisions and forbidden transitions/safeguards give
409; malformed IDs/revisions give 400. Authentication/admission keep their existing responses.
The scoped consumer Contract reauthorizes point reads and repeats; a body cannot choose
actor/Organization or set arbitrary status/grants. No role-assignment/listing UI is supplied.

Self-suspension is blocked and an active administrator is preserved, including concurrent
attempts by two administrators to suspend each other. An accepted change commits one audit
with its membership revision; conflicts/no-ops/denials add no accepted-change audit.
Audit/storage/known-commit failure rolls both back; a lost commit response needs a fresh
authorized state read, not blind retry. Native database errors remain errors, not denial.

Suspension blocks subsequent protected requests with the same issued cookie, without
deleting global identity, other active Organizations or historical attribution. Already
admitted operations retain their immutable context. Reactivation preserves retained grants
subject to current checks and does not restore revoked grants. Public catalog exceptions
remain public. These Access grants do not define private business-data permissions;
Personnel, licensing and complete resource-authorized queries remain separate capabilities.

The route/subdomain composition is unchanged. Hosts using a subdomain can map
MembershipEndpoints.Map with `/memberships/{membershipId:guid}` after their existing setup.

[Program](Program.cs) chooses route selection explicitly through
`AddOrganizationTenancyFromRoute("organization")`. For the tested subdomain alternative,
replace selection registration and endpoint patterns:

```csharp
builder.Services.AddOrganizationTenancyFromSubdomain(baseDomain);
builder.Services.AddHostFiltering(options => options.UseTenantSubdomainHosts(baseDomain));
// After building and calling DemoComposition.ConfigureHttp(app):
DemoComposition.MapOrganizationEndpoints(app, "/catalog", "/tenant-identity", "/stock/{sku}");
CustomerProfileEndpoints.Map(app, "/customers/{customerId:guid}/profile");
```

Both presets call the same persisted lookup/admission implementation. Hostname mode reads
`/catalog`, `/stock/{sku}` and `/tenant-identity` at a selected Organization host such as
`north-supply.wholesale.example.test`. Configure DNS/hosts, certificates and provider
callbacks for your deployment. There is no configuration-driven strategy switch, nullable
domain inference or automatic fallback. Native host filtering includes apex/subdomain
terminal-dot variants; candidate selection rejects nested prefixes. Proxy trust is still
explicit consumer configuration.

## Endpoint and module ownership

| Endpoint | Behavior |
| --- | --- |
| `/health` | Anonymous, tenantless allowed; liveness string, not database readiness. |
| `/health/live` | Anonymous, tenantless; native self check, independent of database availability. |
| `/health/ready` | Anonymous, tenantless; native self check plus connectivity/required module tables. Never migrates or repairs. |
| `/identity` | Native authentication required; tenantless allowed; persisted application actor. |
| `/public-identity` | Anonymous, tenantless allowed; mapped authenticated actors remain identified. |
| `GET /antiforgery` | Native authentication, tenantless allowed; issue a non-cacheable native token/cookie pair bound to the application actor. |
| `GET/PUT /organizations/{organization}/customers/{customerId:guid}/profile` | Native authentication and active membership; read or versioned edit. PUT explicitly validates antiforgery before Sales work. |
| `/login` | Anonymous, tenantless allowed; native OIDC challenge with a fixed local return path. |
| `/organizations/{organization}/catalog` | Tenant required; explicit public Organization access for anonymous callers and mapped non-members. |
| `/organizations/{organization}/identity` | Native authentication and tenant required; current active membership. |
| `/organizations/{organization}/stock/{sku}` | Native authentication and active membership; persisted tenant-isolated availability read. |
| `/catalog`, `/stock/{sku}`, `/tenant-identity` | Subdomain alternatives with the same admission policies. |

[Access](../modules/Access/Access/ApplicationAccess.cs) owns global user/external-account lookup, canonical
Organization lookup and membership admission through its [query Contract](../modules/Access/Access.Contracts/IApplicationAccess.cs).
Its [DbContext](../modules/Access/Access/Persistence/AccessDbContext.cs) queries before tenant establishment,
so it uses explicit keys rather than a current-tenant filter or filter bypass. Member
admission joins the selected Organization and current active membership in one statement.

Issuer/subject pairs compare exactly; email claims grant no identity link. Organization
slugs use lowercase ASCII DNS-compatible labels, accept ASCII case during lookup, and reject
spaces/underscores instead of rewriting them. Human actor keys and tenant keys remain the
same stable application identifiers regardless of external provider or selection strategy.

Every selected Organization requires member admission by default. The catalog explicitly
adds [public-access metadata](HttpIntegration/PublicOrganizationAccessAttribute.cs) using
`.AllowPublicOrganizationAccess()`; `.AllowAnonymous()` alone does not grant Organization
access. This is consumer policy, separate from native authentication and the library's
tenant requirement. Only human actors enter protected member operations in this sample.

Membership is checked afresh for each protected operation. A committed suspension/removal
blocks the next admission even with the same valid cookie. Already-admitted operations keep
their immutable contexts; this is not serializable authorization through a later business
commit. Membership status and authority are not cached in the cookie or actor/tenant context.

[Inventory](../modules/Inventory/Inventory/StockCatalog.cs) owns its scoped database read and
[Contract](../modules/Inventory/Inventory.Contracts/IStockCatalog.cs). Non-HTTP callers explicitly establish trusted
identity, invoke Access admission when required, and initialize their tenant before business
work. Context establishment itself grants no permission. Separate
[module projects](../modules/README.md) own implementation and Contracts; HTTP bridges remain
in the host. Inventory explicitly registers `HasTenantOwnership` and calls
`ValidateTenantChanges` in native saves, with no filter bypass.

[DemoComposition](DemoComposition.cs) keeps routing, native authentication/authorization,
actor completion, tenant admission and endpoint work in the reviewed order. The consumer's
[exception handler](ContextExceptionHandler.cs) returns generic ProblemDetails: actor mapping
failure 403, required selection absent 400, unknown/inaccessible Organization 404. Unrecognized
database faults use native 500 handling. Nothing re-executes identity/tenant establishment.

[NativeAuthentication](NativeAuthentication.cs) retains editable code-flow/PKCE/raw-claim/
secure-cookie configuration. Removing native `iss` deletion preserves the validated issuer
for mapping; token validation remains native.

## Versioned profile edit

Full setup supplies customer `10000000-0000-0000-0000-000000000001` in Alpha and
`10000000-0000-0000-0000-000000000002` in Beta. The GET profile supplies AddressId and
Version; do not invent an address or overwrite a newer version. Any active human member
can edit for this demonstration; roles/permissions are future consumer policy.

A same-origin HTTPS browser client with its native authentication cookie can use:

```javascript
const path = "/organizations/north-supply/customers/10000000-0000-0000-0000-000000000001/profile";
const profile = await (await fetch(path)).json(); // Check success in application UI.
const { requestToken } = await (await fetch("/antiforgery")).json();
const response = await fetch(path, {
  method: "PUT",
  headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": requestToken },
  body: JSON.stringify({
    addressId: profile.addressId,
    expectedVersion: profile.version,
    displayName: "Updated Customer",
    addressLine: "Updated Street",
  }),
});
// 200 contains the committed profile. On 409, reload and let the user resolve the conflict.
```

The host's [native filter](HttpIntegration/ValidateAntiforgeryFilter.cs) is explicitly attached
to this PUT. It validates the native cookie/header pair after authentication, actor mapping
and fresh Organization admission. Its [additional-data provider](HttpIntegration/ActorAntiforgeryData.cs)
binds to the established human ActorId as well as native principal checks. Acquire a fresh
token after changing login identity. The same actor may use a token in another independently
admitted Organization; it grants no membership. Tokens stay out of URLs/logs.
The antiforgery cookie is host-only, HttpOnly, Secure and SameSite.Strict. Subdomain alternatives
use their own host cookies; real browser/OIDC/proxy topology requires later testing.

Invalid input/protection returns 400; missing customer or wrong tenant/customer/address pair
returns 404; stale/competing edits return 409. DisplayName is nonblank and at most 256 characters;
AddressLine is nonblank and at most 512; accepted text is preserved. ExpectedVersion is
1 through long.MaxValue-1. Sales owns two native saves in one transaction, increments the
customer version, and returns success after commit. Database failure or controlled cancellation
before commit rolls back both rows. A cancelled/lost HTTP response alone cannot establish
whether a transaction committed; no durable receipt or idempotency contract is provided.

## Template material, proofs and limits

The executable setup, module/Contracts projects, native registration, public exception,
HTTP resolvers, owned schemas/histories and response policy are editable template recipes. Generated template
files and bootstrap CLI remain E10. Neither technical core nor HTTP adapter depends on Access.

[Composition proofs](../IntegrationTests/Api/CompositionTests.cs),
[admission proofs](../IntegrationTests/Api/AdmissionTests.cs) and
[persistence/setup proofs](../IntegrationTests/Api/PersistenceTests.cs) and
[persisted catalog proofs](../IntegrationTests/Api/CatalogTests.cs) run on disposable
PostgreSQL 18.6 Testcontainers with the resource reaper enabled. They use native TestServer
and protected cookies without contacting an OIDC provider. They exercise real migrations,
current-membership revocation, public non-member access, route/subdomain parity, constraints,
tenant-owned business reads, real query cancellation/faults and finite setup child processes.
Two [native telemetry cases](../IntegrationTests/Api/TelemetryTests.cs) exercise the actual
ServiceDefaults with in-memory exporters, correlated request/database Activities and failure
logs, plus native request metrics. The separate [runtime suite](../RuntimeComposition.Tests/RuntimeTests.cs)
proves Kestrel graph startup/manual setup and readiness during a database outage.
The compatibility proof starts from retained checkpoint SQL and preserves existing Access
rows/history through module relocation and Inventory migration.

Run the suite in the active PostgreSQL lane; it is no longer a container-free hook.
Standalone actor/tenancy HTTP suites remain container-free. See
[development commands](../../../docs/development.md) and
[the E3.3 report](../../../docs/reports/e3-3-persisted-access.md) and
[the E3.4 report](../../../docs/reports/e3-4-persisted-business-ingress.md) and
[the E3.5 report](../../../docs/reports/e3-5-profile-mutation.md).
[Mutation proofs](../IntegrationTests/Api/ProfileTests.cs) and
[Sales persistence proofs](../IntegrationTests/Api/ProfilePersistenceTests.cs) add real native
antiforgery, exact application-actor rebinding, tenant/pair isolation, stale/competing edits,
second-save database failure and cancellation at a blocked address UPDATE.

The [real browser journeys](../RuntimeComposition.Tests/BrowserTests.cs) exercise local
Keycloak login/callback and native session cookies against HTTPS Kestrel, with no injected
authentication cookie or OIDC form replay. Their scope/limits are recorded in
[E3.7](../../../docs/plans/e3-7-oidc-browser-journey.md).

Focused membership administration and private draft grants are supported as documented here.
User creation/linking, invitations, general grant administration and configurable roles,
logout/provider end-session, session invalidation, external provider/proxy topology, tenant-aware
native authorization handlers and shared module transactions remain separate increments.
The catalog read alone establishes no reservation/workflow guarantee; those have separate
consumer proofs. E3.7's dated browser-provider evidence does not prove later permission features.

## Private draft permissions

Explicit `--initialize-demo` setup now seeds a private draft with ID
`a2000000-0000-0000-0000-000000000001` in each Organization. Alpha's North draft totals 40
and is editable by `application-alpha`; Beta's South draft totals 70 and is editable by
`application-beta`. Alpha's active South membership grants no access to Beta's draft.
The fixed ID collision demonstrates that resource identity always includes Organization.
Apply migrations through explicit setup, never HTTP startup. Existing databases receive no
inferred grants for historical drafts; the finite demo initializer requires a disposable DB.

| Route under `/organizations/{organization}` | Consumer behavior |
| --- | --- |
| `GET /draft-orders?minimumTotal=30&limit=1` | Sales combines resource grants and business filters before count/page; unauthorized rows never fill the page or totals. Other optional filters are `customerNameContains`, `currency`, `offset`. |
| `GET /draft-orders/{draftId}` | Late native `IAuthorizationService` Read check, followed by the module's own permission-filtered detail read. Unknown/inaccessible resources both return 404. |
| `PUT /draft-orders/{draftId}/line` | Native authentication/admission/antiforgery, late Edit check, then module Edit enforcement and native Sales save/commit. JSON: `{"expectedVersion":1,"lineId":"a2000000-0000-0000-0000-000000000002","quantity":2,"unitPrice":25}`. Success returns version 2/total 50; stale versions return 409. |

Read/Edit grants are positive and scoped to a canonical human/system kind+key; Edit includes
Read. Membership administrators receive no implicit grant. The host's boolean resource
decision is not cached authority: direct module calls enforce grants too. HTTP admission
still checks current Active membership before tenant publication; already-admitted contexts
stay immutable. Native challenge/forbid and the public catalog exception retain their order.

Direct callers establish trusted actor/tenant contexts, perform fresh human admission or
select an explicit system mandate, and call the same Contracts. New draft creation stages
the creator's Edit grant in its existing transaction. System read requires its own grant,
and a human initiator contributes no authority. Sales commands retain their human requirement.
The response's lines/customer display fields share draft visibility in this bounded sample.

Count and page use separate native statements; choose RepeatableRead for one snapshot when
required. Grant changes can affect a later statement while the actor/tenant stay fixed.
This permission capability supplies no permission-at-commit fencing, grant administration/audit, export/AI selection
or a role/group engine. See the [Sales capability guide](../modules/Sales/README.md),
[scope](../../../docs/plans/a2-1-sales-resource-permissions.md) and
[proof report](../../../docs/reports/a2-1-sales-resource-permissions.md).

## Optional Sales HTTP row security

Supplying `ConnectionStrings:Sales` selects the optional
[PostgreSQL capability](../../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
for **all profile/draft HTTP requests**. It retains EF filters and Sales resource predicates.
Tenant admission still runs once in the existing HTTP middleware order. Endpoint reads and
late resource checks begin short native Sales transactions; profile writes and draft edits
retain their existing transaction owners. The interceptor supplies the admitted tenant before
the first protected statement. No endpoint calls a tenant-database setup helper or creates
another operation scope. Native command failures still produce server errors.

| Sales table | Runtime privileges; database boundary |
| --- | --- |
| `customer_profiles`, `customer_addresses`, `draft_orders`, `draft_order_lines` | SELECT/INSERT/UPDATE/DELETE, tenant equality through USING and WITH CHECK. |
| `draft_order_grants` | SELECT only, tenant equality; this HTTP composition edits existing drafts and does not create grants. |
| `audit_entries` | SELECT/INSERT, tenant equality; accepted profile audit commits with its business rows. |
| `outbox_messages` | SELECT/INSERT, tenant equality; complete public directory updates commit with profile changes. No HTTP dispatch/update/delete permission. |
| Other Sales tables, including inbox/outbox/workflows | No runtime table privileges; no new RLS policy claim. |
| Access/Inventory | Separate restricted connection for global admission and catalog work; no Sales table privileges. No new RLS policy. |

Use a fresh disposable database, a migration-owner login and two separate runtime logins.
Provision those logins independently, without superuser/BYPASSRLS/CREATEROLE/CREATEDB,
privileged role memberships or Sales schema CREATE. Give the owner database/schema creation
authority for finite migrations. Do not reuse the AppHost's default PostgreSQL admin credential.
There is no automatic role/password creation.

The separate [Aspire opt-in](../AppHost/README.md#optional-sales-rls-graph) supplies a finite
`--initialize-rls-demo` mode that creates the two named disposable-demo runtime logins before
the migrations/seed/policy steps. That mode alone receives `ConnectionStrings:Setup`; it is
not called by ordinary HTTP startup. Use that graph's workflow instead of the manual role
provisioning steps below. Normal RLS HTTP startup rejects nonempty setup/migration/admin
parameter credentials; empty overrides are used to remove known inherited AppHost values.

1. Run `--initialize-demo` with `ConnectionStrings:Access` temporarily supplied as the owner
   connection. This applies the ordinary module migrations and seeds before RLS is installed.
2. As the owner, grant the Access runtime role USAGE on `access`/`inventory`, SELECT/INSERT/
   UPDATE/DELETE on the existing Access tables, and SELECT on `inventory.stock_availability`.
   For example, for a separately provisioned `wholesale_access` login:

   ```sql
   GRANT USAGE ON SCHEMA access, inventory TO wholesale_access;
   GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA access TO wholesale_access;
   GRANT SELECT ON inventory.stock_availability TO wholesale_access;
   ```

3. Set `ConnectionStrings:Access` to that restricted login, `ConnectionStrings:Sales` to
   the separate Sales login, and `ConnectionStrings:SalesMigrations` to the table owner.
   All three connections target that same database. Run:

   ```bash
   dotnet run --project samples/Wholesale/Api/Wholesale.Api.csproj -- --initialize-sales-rls
   ```

4. Remove the migration credential from HTTP configuration and start HTTP normally.

The finite [installer](SalesHttpRowSecurity.cs) applies explicit ENABLE/FORCE and USING/
WITH CHECK operations plus only the listed Sales grants in one transaction. It does not
change ordinary EF migration history, reconcile existing policies or support repeated setup.
Keep this database separate from the earlier CQRS five-table RLS demonstration. Future
policy changes need reviewed consumer migrations; model declarations do not scaffold policies.

Startup checks the actual two runtime credentials, tenantless Sales baseline, finite table
flags, policy shape/count and required/forbidden privileges. A failed check stops startup.
This is a bounded catalog check, not certification of policy expression bodies, views/functions,
other secrets, or subsequent administrative changes. Review those separately. Readiness retains
its existing connectivity/schema check; it does not continually re-attest RLS policy.
Never pass migration credentials to the HTTP host or enable these credentials in transport
workers. The standard AppHost graph retains its existing credentials; its separate RLS opt-in
now provisions restricted credentials through explicit setup. Default single-connection
HTTP mode remains supported and also uses the explicit read transactions; it enables no RLS.

RLS supplies database tenant equality, not private-draft permissions, authentication or
membership freshness. A credential holder can forge the custom tenant setting; same-tenant
private rows still require Sales predicates. Missing/tenantless context and missing caller
transactions reject protected EF work. DbContext pooling, external poolers and failover are
not proved by this composition. Direct module callers in this selected mode must begin their
own native Sales transaction around reads, resource checks and writes.

[HTTP/PostgreSQL proofs](../IntegrationTests/Api/RowSecurityTests.cs) exercise native HTTP
admission, actual restricted roles, Alpha/Beta/Alpha backend reuse, deliberately unfiltered SQL,
exact varchar keys, read/list/edit permissions, raw write rejection and profile/audit rollback.
The [slice report](../../../docs/reports/a2-6-sales-http-rls.md) records verification and limits.
