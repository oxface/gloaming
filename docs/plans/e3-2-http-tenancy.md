# E3.2 tenant establishment at HTTP ingress

Status: owner-reviewed and checkpointed as `cfbac9a`, 2026-10-05,
after the owner-reviewed E3.1
checkpoint `faefc0b`. The owner confirmed that this first tenancy adapter runs after native
authorization and actor establishment, before endpoint work. Tenant-aware native authorization
handlers are deferred. [The implementation report](../reports/e3-2-http-tenancy.md) records
executed evidence separately from the approved scope below.

## Outcome and extraction hypothesis

Propose `ModulithFoundry.Tenancy.AspNetCore`, an optional adapter referencing only Tenancy and
the native `Microsoft.AspNetCore.App` framework. It requires neither ActorIdentity nor its
HTTP adapter, EF, OIDC, Access, transport or Aspire. The two foundation cores remain unchanged.

The candidate reusable mechanism is asynchronous establishment of a canonical, admitted
tenant before endpoint work, enforcing a consumer-configured default and explicit endpoint/
group exceptions independently of native authorization-policy selection. Optional candidate
utilities cover a named route value and one subdomain label beneath an explicit base domain.
Consumers can supply other strategies without using either utility.

The consumer owns canonical identity lookup, admission, Organization/membership meaning,
native authentication/authorization, failure responses and permission checks. A candidate is
untrusted input, not a `TenantId` and not proof of membership. One application user can select
different Organizations in separate requests without changing their actor identity.

## Evidence and scope choice

The archived [OrganizationScopeMiddleware](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/apps/Api/Modules/Access/Middleware/OrganizationScopeMiddleware.cs)
runs after native authorization in [the old host](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/apps/Api/Program.cs).
It reads one fixed route value, resolves the current user's Organization access and attaches
a combined user/Organization/membership/role context. Its focused
[HTTP unit cases](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/tests/ApplicationTests/OrganizationScopeMiddlewareTests.cs)
exercise successful attachment and inaccessible Organization handling. The old
[query implementation](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Access/Access/Organizations/Queries/OrganizationQueries.cs)
performs canonical slug lookup and active-membership admission together. These are historical
source findings; no archived tenancy tests were rerun for this proposal.

Retain the after-authorization ordering and consumer admission seam. Replace the fixed route,
combined context, opt-in-only scope metadata and built-in 404 response with configurable
consumer selection, the independent tenant accessor, explicit requirements and host responses.

Native authorization can short-circuit a request with challenge/forbid, and anonymous
metadata permits the downstream pipeline. Tenancy middleware therefore runs only when that
pipeline continues, including named/permissive policies and policy-free requests.
[Native authorization middleware, 10.0.12](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Security/Authorization/Policy/src/AuthorizationMiddleware.cs).

This slice deliberately does not make tenancy available inside native authorization handlers.
They may use the E3.1 actor accessor, but must not read an uninitialized tenant accessor.
Tenant-dependent membership admission happens in the resolver; tenant-dependent capability
permissions run after establishment through explicit consumer checks. A later integration
slice can establish tenancy before native permission handlers if a real consumer needs it.
There is no second tenancy evaluator, automatic evaluator decoration or new common HTTP
context framework. Native actor registration retains its existing evaluator.

## Proposed public interface

The declarations below are interface sketches, not compilable implementation files.

```csharp
public interface IHttpTenantContextResolver
{
    ValueTask<TenantContext?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

public enum TenantRequirement
{
    Required = 1,
    TenantlessAllowed = 2,
}

public sealed class HttpTenantContextOptions
{
    public TenantRequirement DefaultRequirement { get; set; } = TenantRequirement.Required;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = false, Inherited = true)]
public sealed class TenantRequirementAttribute : Attribute
{
    public TenantRequirementAttribute(TenantRequirement requirement);
    public TenantRequirement Requirement { get; }
}

public sealed class TenantContextMiddleware
{
    public TenantContextMiddleware(
        RequestDelegate next,
        IOptions<HttpTenantContextOptions> options);

    public Task InvokeAsync(
        HttpContext context,
        IHttpTenantContextResolver resolver,
        ITenantContextInitializer initializer);
}

public sealed class HttpTenantResolutionException : Exception
{
    public HttpTenantResolutionException(string message);
}

public static class HttpTenantCandidates
{
    public static string? FromRoute(HttpContext context, string routeValueName);
    public static string? FromSubdomain(HttpContext context, string baseDomain);
}

public static class HttpTenantContextExtensions
{
    public static IServiceCollection AddHttpTenantContext<TResolver>(
        this IServiceCollection services,
        Action<HttpTenantContextOptions>? configure = null)
        where TResolver : class, IHttpTenantContextResolver;

    public static IApplicationBuilder UseHttpTenantContext(this IApplicationBuilder app);

    public static TBuilder RequireTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder;

    public static TBuilder AllowTenantless<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder;
}
```

Registration supplies one scoped `TenantContextAccessor`, both core interface aliases,
a scoped consumer resolver and native options configuration/validation. Undefined requirement
values, including 0, fail configuration; attribute construction rejects them as well.
Call registration and middleware setup once. Direct holder/middleware composition remains
available for alternate service construction. No native authentication/authorization services,
schemes, policies, resolver discovery or `IPolicyEvaluator` registration are added.

## Requirement and resolver semantics

The default applies to matched endpoints without tenancy metadata. The most significant
`TenantRequirementAttribute` overrides it. Minimal API helpers attach that same metadata;
controller/action attributes and group metadata share the contract. Use native
`GetMetadata<TenantRequirementAttribute>()`, following ordered endpoint metadata rather than
inventing a separate precedence engine. Inner-group/endpoint declarations override outer
groups; later metadata at the same level wins.
[Native metadata collection, 10.0.12](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Http/Http.Abstractions/src/Routing/EndpointMetadataCollection.cs),
[native group metadata ordering](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0#route-groups).

`AllowTenantless()` permits an explicitly tenantless result. It neither skips resolution nor
forces tenantless execution. A successfully resolved tenant is retained. `AllowAnonymous`
changes no tenancy requirement; a public catalog can require a tenant, and an authenticated
operation can explicitly allow tenantless execution. Native named/default/fallback policies
cannot remove the separate tenancy requirement.

The resolver runs once for each matched request that reaches this middleware. It extracts a
candidate using its chosen strategy, resolves canonical identity and completes admission
before returning. An absent candidate can deliberately produce `TenantContext.Tenantless()`;
unknown, malformed or denied candidates must fail rather than fall back to tenantless
execution. This mapping distinction is the consumer resolver's responsibility: the adapter
cannot infer the meaning of an arbitrary resolver's input or admission policy.

| Resolver outcome | Adapter behavior |
| --- | --- |
| `TenantContext.ForTenant(...)` | Initialize once, then invoke downstream work. |
| Explicit tenantless result, tenantless allowed | Initialize deliberate tenantless context, then continue. |
| Explicit tenantless result, tenant required | Call the existing core `RequireTenant()` before publication; propagate `TenantRequiredException`. |
| Null, under either requirement | Throw `HttpTenantResolutionException`; publish nothing and do not invoke downstream work. |
| Consumer admission exception or unexpected fault | Propagate unchanged; no retry or library response selection. |
| Cancelled resolution | Propagate cancellation without tenant publication or downstream work. |

Use `RequestAborted` at entry, pass it into resolution, and check again before publication.
Skip requests with no matched endpoint, leaving native unmatched-route behavior intact.
Resolver code does not authenticate, change the request principal, replace the endpoint or
mutate routing/host information. Capture the principal before awaiting resolution and reject
replacement during it. The host keeps identity and routing stable for the rest of the operation.

This adapter has one establishment entry point. It needs no successful-publication feature
to coordinate a second path. Register it once; repeated execution in the same operation scope
fails the existing single-assignment rule rather than resetting context. Identity-changing
or tenant-changing route re-execution and nested operation rebinding remain unsupported.

Actor and tenant publication are not atomic together. If actor establishment succeeded and
tenant admission fails, business work does not start; the host aborts/disposes the scope.
The adapter does not undo or rebind the actor holder.

## Candidate utility limits

`FromRoute` reads only the explicitly named route value after routing. Missing/null means no
candidate. Present values must be nonblank strings; malformed input throws the typed resolution
failure. Accepted text is preserved without trimming, lowercasing, GUID parsing or slug rules.
The consumer may use native route APIs directly when different value handling is wanted.

`FromSubdomain` reads only `Request.Host.Host`, which omits the port. It supports one ASCII DNS
label immediately beneath an explicit ASCII DNS base domain, with comparison ignoring DNS
case and an optional terminal dot handled consistently. It preserves candidate label case;
canonical Organization lookup remains consumer-owned. Apex host means no candidate. Wrong
suffixes, multiple prefix labels, blank/malformed labels and IP hosts fail resolution; they
are not absence and do not become tenantless. Invalid base-domain configuration fails as an
argument/configuration error. Wildcard configuration, arbitrary custom domains, Unicode/IDN
mapping and multiple base domains are outside this first helper; custom resolvers remain open.
[Native HostString implementation, 10.0.12](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Http/Http.Abstractions/src/HostString.cs).

Neither utility grants admission or selects the first available strategy. The consumer
explicitly chooses route, subdomain or another resolver, such as application-user lookup.
No automatic route-to-host-to-claim fallback chain is added.

Host filtering and trusted forwarded-header handling belong to native consumer setup before
selection. The utility never reads raw `X-Forwarded-Host`, installs a proxy policy or treats a
hostname as membership. End-to-end proxy infrastructure is not proven by extracting a label.
[Native host filtering](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/host-filtering?view=aspnetcore-10.0),
[native trusted proxy configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

## Consumer and template recipe

```csharp
// Native authentication/authorization and actor mapping remain consumer-selected.
services.AddHttpActorContext<ApplicationActorResolver>();
services.AddRouteTenancy<OrganizationTenantResolver>("organization");

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpActorContext();
app.UseHttpTenantContext();

app.MapGet("/health", Health).AllowAnonymous().AllowTenantless();
app.MapGet("/identity", Identity).RequireAuthorization().AllowTenantless();
app.MapGet("/organizations/{organization}/catalog", Catalog).AllowAnonymous();
```

An alternative host can set `DefaultRequirement = TenantRequirement.TenantlessAllowed` and
opt selected endpoint groups into `RequireTenant()`. A tenancy-only host registers its own
resolver and the tenancy helper without any actor registration/reference. The resolver may
use a consumer application user or established actor when admission needs it; that dependency
belongs to the consumer rather than the tenancy adapter.

Extend `HttpIdentityDemo` with this recipe rather than adding another near-identical host.
Its existing identity/login/health endpoints explicitly allow tenantless execution. Add a
public Organization-scoped Inventory catalog read and a protected identity/tenant read.
The sample fixture admits known Organizations to those demonstration reads; it does not
implement or claim durable membership. The same mapped application actor can select Alpha
and Beta in separate requests while the catalog exposes independently expected tenant data.

Freshly record sample ownership: Access owns the configured Organization directory and
canonical resolution; Inventory owns fixture catalog data and its query Contract. Host code
composes them, presenting failures outside the library. Keep module Contracts and these
business fixtures under the sample; retain a core tenant guard at the Inventory capability.
No archived business source or fixtures move into a technical library.

Demonstrate route selection by default and an explicitly configured subdomain alternative,
with no strategy fallback. Hostname mode uses native host configuration and matching request
hosts; it does not require DNS provisioning or a fake production authentication scheme.
Existing native OIDC setup remains editable consumer code; remote login is still unproven.

Template output is this executable selection/registration/pipeline and override recipe,
including consumer-owned directory, admission/failure policy and native host configuration.
No generated template or bootstrap CLI is introduced. The HTTP sample now composes actor
and tenancy; actor-only independence remains exercised by the separate E3.1 HTTP suite.

## Focused implementation proofs

Create `src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests` referencing only the new adapter, native framework and
TestHost/xUnit. Extend the existing HTTP sample suite for real actor/tenant composition.
Do not add registration-descriptor assertions or tests for native metadata/DI in isolation.

| Case | Required observable evidence |
| --- | --- |
| Tenant default | A matched endpoint without tenant metadata rejects absent selection before business invocation; a valid canonical tenant reaches the endpoint. |
| Anonymous and named policies | Public/permissive/named-policy endpoints retain the configured tenant requirement. `AllowAnonymous` alone does not permit tenantless work. |
| Explicit exception | Missing selection establishes tenantless only where permitted; valid selection is retained on that endpoint; unknown/denied selection still fails. |
| Overrides | Exercise outer group, inner group and endpoint overrides in both directions, plus a routed controller/action attribute, asserting the resulting context or rejection. |
| Configurable default | A host permitting tenantless by default still enforces an explicitly required endpoint/group; invalid enum configuration fails startup. |
| Native denial | Missing credentials or native permission denial preserves native challenge/forbid and never invokes tenant resolution/business work. |
| Selected scheme | Resolve only after policy-selected authentication; a consumer resolver observes the selected principal rather than the default identity. |
| Canonical route mapping | A configurable route name maps a slug to an independently expected canonical tenant key; malformed and unknown input does not expose another tenant's data. |
| Subdomain mapping | Exercise a different base domain, host case/port/terminal-dot handling, apex absence and wrong-boundary/nested-prefix failures through resulting tenant context. Raw forwarded headers do not change the selected tenant. |
| Custom selection | A consumer-defined user-to-tenant strategy works without either candidate helper or actor library. |
| Failure/cancellation | Null, thrown admission/fault and an awaiting cancellation publish no tenant and invoke no business work; a fresh request succeeds. |
| Scope isolation | Overlapping Alpha/Beta requests retain independent contexts; a later permitted tenantless request inherits no tenant. |
| Actor composition | The same application actor appears unchanged in separate Alpha/Beta scopes; anonymous catalog reads have explicit anonymity and a selected tenant. |
| Actual business usage | Inventory's guarded query returns independently expected Alpha/Beta fixture data through its consumer Contract. |
| Unmatched request | The adapter adds no selection failure or resolver invocation for an unmatched endpoint. |
| Adoption | Native project declarations and compiled dependency rules preserve core/adapter independence from actor, EF and sample types; existing actor-only proofs still pass. |

Wire the new suite into the active container-free CI lane and hooks. Run the relevant HTTP
and architecture suites, active build/style/analyzers and repository formatting; report actual
counts and results. Existing PostgreSQL ownership guarantees are historical for this adapter
unless a new persistence consumer is added and rerun; fixture catalog reads do not prove them.

## Review and subsequent scope

### Owner-approved review refinement

The owner requested explicit registration-time route/subdomain selection, common-flow
registration helpers and native global ProblemDetails handling during line-by-line review.
Provide `AddRouteTenancy<TResolver>(routeValueName, configure)` and
`AddSubdomainTenancy<TResolver>(baseDomain, configure)`, using a consumer
`IHttpTenantCandidateResolver.ResolveAsync(string? candidate, HttpContext, CancellationToken)`
for asynchronous canonical lookup/admission. Keep `AddHttpTenantContext<TResolver>` and its
full-request resolver for custom strategies, including consumer user-to-tenant selection.
All three compose with the existing single `UseHttpTenantContext()` middleware. No actor
bridge or strategy discovery is introduced.

Add an explicit native-options utility
`HostFilteringOptions.UseTenantSubdomainHosts(baseDomain)` that replaces the allowed-host
list with the validated apex/subdomain patterns, including terminal-dot forms. The consumer
chooses to call native `AddHostFiltering`; tenancy registration does not silently change
host/proxy policy. Share base-domain validation so invalid preset configuration fails at
registration, rather than only when a request arrives.

Replace sample configuration-driven strategy inference with explicit startup registration
and endpoint mapping. Remove `OrganizationSelection`; the consumer resolver receives the
candidate directly. Replace inline catches with a consumer-owned native `IExceptionHandler`,
`AddProblemDetails` and direct `UseExceptionHandler()` handling without route re-execution.
Database-backed Organizations and membership still belong to the next Access slice. Prove
this refinement at the already-reviewed HTTP/sample seams; do not add descriptor snapshots
or isolated native-framework tests.

The owner reviewed the resolver outcomes, default/override semantics, candidate-helper
limits, registration refinement and consumer/proofs before checkpoint `cfbac9a`.
The plan itself supplies no executable evidence; the report records the proven mechanism.

After this slice, [E3.3 proposes durable Access lookup/admission](e3-3-persisted-access.md),
revocation semantics and a separately bounded increment for
tenant-bearing state-stored business ingress. Native tenant-aware authorization handlers,
membership management, invitations/roles, real provider/proxy topology, BFF session/antiforgery,
and shared cross-module transactions each require separately bounded evidence. Do not bundle
them into this first middleware increment.
