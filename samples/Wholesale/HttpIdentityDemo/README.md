# Wholesale HTTP Access and Organization demonstration

A runnable consumer composing the optional [actor](../../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/README.md)
and [tenancy](../../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/README.md) adapters with native
cookie/OIDC authentication and separate PostgreSQL-backed Access/Inventory/Sales modules.
Business reads and profile edits use module Contracts and tenant-owned rows.

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
causation or trace IDs; native HTTP telemetry remains separate. ConfigurePostgresAudit maps JSONB; native module
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
| `ConnectionStrings:Access` | Shared PostgreSQL connection passed explicitly to Access, Inventory and Sales. |
| `Oidc:Authority` | HTTPS OIDC provider authority for HTTP login. |
| `Oidc:ClientId` | Registered authorization-code client. |
| `Oidc:ClientSecret` | Credential when that client registration requires one. |
| `DemoIdentity:Issuer`, `DemoIdentity:AlphaSubject`, `DemoIdentity:BetaSubject` | Optional finite-setup mapping inputs. Supply all three together, with HTTPS issuer and distinct subjects. |

Set `ConnectionStrings__Access` to a disposable, fresh database and run the finite setup:

```bash
dotnet run --project samples/Wholesale/HttpIdentityDemo/HttpIdentityDemo.csproj -- --initialize-demo
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
dotnet run --project samples/Wholesale/HttpIdentityDemo/HttpIdentityDemo.csproj -- --urls https://localhost:7443
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

[Composition proofs](../HttpIdentityDemo.Tests/CompositionTests.cs),
[admission proofs](../HttpIdentityDemo.Tests/AdmissionTests.cs) and
[persistence/setup proofs](../HttpIdentityDemo.Tests/PersistenceTests.cs) and
[persisted catalog proofs](../HttpIdentityDemo.Tests/CatalogTests.cs) run on disposable
PostgreSQL 18.6 Testcontainers with the resource reaper enabled. They use native TestServer
and protected cookies without contacting an OIDC provider. They exercise real migrations,
current-membership revocation, public non-member access, route/subdomain parity, constraints,
tenant-owned business reads, real query cancellation/faults and finite setup child processes.
Two [native telemetry cases](../HttpIdentityDemo.Tests/TelemetryTests.cs) exercise the actual
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
[Mutation proofs](../HttpIdentityDemo.Tests/ProfileTests.cs) and
[Sales persistence proofs](../HttpIdentityDemo.Tests/ProfilePersistenceTests.cs) add real native
antiforgery, exact application-actor rebinding, tenant/pair isolation, stale/competing edits,
second-save database failure and cancellation at a blocked address UPDATE.

The [real browser journeys](../RuntimeComposition.Tests/BrowserTests.cs) exercise local
Keycloak login/callback and native session cookies against HTTPS Kestrel, with no injected
authentication cookie or OIDC form replay. Their scope/limits are recorded in
[E3.7](../../../docs/plans/e3-7-oidc-browser-journey.md).

User creation/linking, invitations, membership administration, roles/permissions,
logout/provider end-session, session invalidation, external provider/proxy topology, tenant-aware
native authorization handlers and shared module transactions remain separate increments. Inventory availability does not establish reservation
logic or the future event-sourced stock model. The bounded E3 ingress scope is implemented
through E3.7 and pending owner review; these remaining capabilities are not implied by it.
