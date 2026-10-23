# HTTP tenant establishment

An optional adapter for [Tenancy](../Gloaming.Tenancy/README.md), referencing only that
core and the native ASP.NET Core framework. It works without ActorIdentity, EF, an identity
provider or the sample Access model. The consumer supplies resolution and admission.

## Explicit composition

```csharp
services.AddRouteTenancy<OrganizationTenantResolver>("organization");

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
// If adopted, complete actor establishment here with UseHttpActorContext().
app.UseHttpTenantContext();

app.MapGet("/health", Health).AllowAnonymous().AllowTenantless();
app.MapGet("/organizations/{organization}/catalog", Catalog).AllowAnonymous();
app.MapGroup("/tenantless-tools").AllowTenantless();
```

The registration presets group one scoped holder, its read/initialization aliases, candidate
selection, a scoped consumer lookup/admission resolver and validated native options. They register no authentication,
authorization policy or evaluator. Call registration and middleware setup once. Direct
registration and `TenantContextMiddleware` remain available for alternate composition.

`TenantContextMiddleware` runs after native authorization and any adopted actor completion.
Native challenges/forbids happen first and do not resolve a tenant. Establishment includes
the consumer resolver's explicit admission checks before downstream work. The tenant is
**not available inside native authorization handlers** in this composition.

## Resolver contract

For route/subdomain presets, implement
`IHttpTenantCandidateResolver.ResolveAsync(string? candidate, HttpContext, CancellationToken)`
to map the supplied untrusted candidate and complete admission. Null candidate means absence;
the consumer deliberately chooses whether that produces tenantless execution. Malformed
candidate extraction fails before the consumer resolver is called.

For a custom strategy, use `AddHttpTenantContext<MyResolver>()` and implement
`IHttpTenantContextResolver.ResolveAsync(HttpContext, CancellationToken)` to own selection
as well. This remains the escape hatch for user-to-tenant lookup and other strategies.
Both interfaces share the result contract:

| Result | Meaning |
| --- | --- |
| `TenantContext.ForTenant(tenant)` | Canonical admitted tenant; publish once and continue. |
| `TenantContext.Tenantless()` | Deliberate absence; publish only when tenantless work is permitted. |
| `null` | Resolution/admission failed; throw `HttpTenantResolutionException`, even where tenantless work is permitted. |

Unknown, malformed and denied selections must not become tenantless. The library cannot
infer arbitrary resolver input; the consumer must preserve that distinction. Lookup,
membership, permissions, canonicalization and HTTP failure responses remain consumer-owned.
Consumer exceptions propagate unchanged, without retries or response normalization.

The middleware checks `RequestAborted` at entry and after awaiting resolution, before
publication. It rejects principal replacement during resolution. Keep the principal and
routing/host information stable throughout the operation. Failed resolution publishes no
tenant and invokes no endpoint work; an actor already established by another adapter remains
published until the host aborts/disposes that request scope. Publication is not atomic across
segments. Unmatched endpoints skip tenant establishment. Repeated middleware execution and
route re-execution do not reset/rebind the core's single-assignment context.

## Requirements and overrides

`HttpTenantContextOptions.DefaultRequirement` defaults to `TenantRequirement.Required` (1).
Use `TenantlessAllowed` (2) for a different global policy:

```csharp
services.AddRouteTenancy<MyResolver>("organization", options =>
    options.DefaultRequirement = TenantRequirement.TenantlessAllowed);
app.MapGroup("/organizations/{organization}").RequireTenant();
```

Undefined values, including 0, fail configuration. `.RequireTenant()` and `.AllowTenantless()`
attach `TenantRequirementAttribute`, also usable on controllers/actions. Native ordered
endpoint metadata selects the most significant declaration: inner groups/actions/endpoints
override outer declarations, and later metadata at the same level wins. There is no separate
precedence engine.

`AllowTenantless()` still resolves and retains a valid supplied tenant. `AllowAnonymous()`
does not relax tenancy; named/default/fallback authorization policies cannot remove the
independent requirement. Explicit tenantless results on required endpoints propagate the
core `TenantRequiredException` before publication. Keep tenant guards at capability entry
points for calls that bypass HTTP.

## Optional selection utilities

Use `AddSubdomainTenancy<OrganizationTenantResolver>(baseDomain)` instead of route registration
to choose one-label subdomain selection at startup. Invalid base-domain configuration fails
registration. There is no live strategy switch. Both presets use the same middleware and
consumer resolver; choose exactly one registration path.

Host filtering is a separate, explicit native call:

```csharp
services.AddSubdomainTenancy<OrganizationTenantResolver>(baseDomain);
services.AddHostFiltering(options => options.UseTenantSubdomainHosts(baseDomain));
```

`UseTenantSubdomainHosts` replaces the native allowed-host list with the validated apex and
subdomain patterns, including terminal-dot forms. Consumers can edit/add native host patterns
and retain control of other filtering options and proxy trust. The utility neither performs
tenant admission nor constrains wildcard matching to one label; tenant candidate extraction
enforces that stricter boundary. Ensure native host filtering runs before selection (the
default native web host supplies it; alternate hosts can explicitly use `UseHostFiltering`).

`HttpTenantCandidates.FromRoute(context, routeValueName)` reads one named route value.
Missing/null means no candidate; present values must be nonblank strings. Accepted text is
preserved exactly, without trimming, case conversion or slug/GUID rules.

`FromSubdomain(context, baseDomain)` reads only `Request.Host.Host`, omitting the port. It
accepts one ASCII DNS label beneath an explicitly configured ASCII DNS base domain, comparing
DNS case insensitively and allowing a terminal dot. It preserves label case. The apex means
absence; wrong suffixes, nested prefixes, malformed labels and IP hosts throw the typed
resolution failure. Invalid base-domain configuration throws an argument error. Unicode/IDN
conversion, custom domains and multiple base domains require consumer resolvers.

Neither candidate utility performs admission or chooses a strategy. Native host filtering and trusted
forwarded-header configuration belong to the consumer before selection. Raw `X-Forwarded-Host`
is never read. Native filtering may reject a host before candidate extraction; the opt-in
host-options utility supplies both terminal-dot variants. There is no route/host/claim
fallback chain. A user-to-tenant resolver can use neither helper.

[Standalone HTTP proofs](../tests/TenancyAspNetCoreTests/HttpTenantTests.cs) exercise the
adapter without an actor dependency. [The sample](../../../samples/Wholesale/Api/README.md)
composes both adapters with consumer-owned Organization lookup and guarded Inventory reads.
[The E3.2 report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/e3-2-http-tenancy.md) records executed guarantees and limits.

## Deferred direction

The current route/subdomain/custom resolver choices and overrides are defined above. Unicode/
IDN/custom domains, alternative authorization ordering and route re-execution require explicit
consumer choices and new proofs; no fallback strategy or rebinding is selected. Database RLS,
tenant-value stamping and worker/message propagation belong to separate capabilities and do
not follow from successful HTTP admission.
