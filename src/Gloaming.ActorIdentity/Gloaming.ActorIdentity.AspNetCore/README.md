# Actor identity in ASP.NET Core

An optional adapter for [ActorIdentity](../Gloaming.ActorIdentity/README.md), using the
native `Microsoft.AspNetCore.App` framework. It requires no Tenancy, EF, OIDC package,
sample Access, transport or Aspire dependency. The foundation core remains package-free.

## Interface and explicit setup

- `IHttpActorContextResolver`: consumer mapping from the effective authenticated principal
  into `ActorContext`, with `RequestAborted` cancellation. Null or an anonymous result for
  an authenticated principal produces `HttpActorResolutionException`.
- `ActorContextPolicyEvaluator`: explicitly wraps a consumer-selected `IPolicyEvaluator`.
  It establishes after the inner evaluator authenticates the policy's schemes and before
  authorization handlers run. Authorization and authentication results delegate unchanged.
- `ActorContextMiddleware`: after native authorization, completes establishment for requests
  where no policy invoked the evaluator. Both integration points share successful request
  establishment, so the resolver runs once rather than publishing twice.
- `AddHttpActorContext<TResolver>()`: registers the scoped holder and both aliases, a scoped
  consumer resolver and a transient wrapper around the native `PolicyEvaluator`. An overload
  accepts an explicit inner-evaluator factory for custom native evaluators.
- `UseHttpActorContext()`: adds completion middleware at the consumer-selected pipeline position.

Consumers register native authentication/authorization, then opt into actor establishment:

```csharp
services.AddHttpActorContext<ApplicationActorResolver>();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpActorContext();
```

Call registration and pipeline setup once. The helper registers its own holder, resolver and
`IPolicyEvaluator`; native DI selects these for single-service resolution over earlier
registrations. It does not capture an existing evaluator. Supply a custom inner explicitly:

```csharp
services.AddTransient<CustomPolicyEvaluator>();
services.AddHttpActorContext<ApplicationActorResolver>(provider =>
    provider.GetRequiredService<CustomPolicyEvaluator>());
```

Do not resolve `IPolicyEvaluator` inside that factory: it would recursively resolve the wrapper.
Consumers can instead wire the public holder, wrapper and middleware directly when they need
different service construction. No automatic descriptor decoration, authentication-scheme
registration or new actor policy is introduced. The helpers neither call `AddAuthorization`
nor insert authentication/authorization middleware or choose failure responses.
An evaluator alone misses policy-free requests; middleware alone establishes too late for
authorization handlers. Mapping before policy authentication can capture a different scheme.
Authentication handlers must finish before consuming established actor context.
Business code uses the existing injected `IActorContextAccessor`.

The private request feature records successful establishment and its principal; actor data
stays in the scoped accessor. Features are a type-keyed HTTP extension mechanism shared by
servers and middleware, rather than general application storage. The two integration points
use this marker to coordinate one publication without adding HTTP state to the core.
See [native request features](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/request-features?view=aspnetcore-10.0).

## Guarantees and consumer obligations

| Effective request identity | Behavior |
| --- | --- |
| No authenticated identity | Establish explicit anonymity without calling the resolver; native authorization decides access. |
| Authenticated, mapped | Publish the supplied context once, preserving explicit actor kind and optional initiator. |
| Authenticated, unmapped or mapped to anonymity | Throw the typed mapping failure; do not invoke downstream authorization/business work. |
| Resolution fault or cancellation | Propagate without retry or partial publication. |
| Principal replaced after establishment | Reject reuse rather than retain a stale actor. |

Native `RequireAuthorization`, `[Authorize]`, named/default/fallback policies and anonymous
metadata keep their native authorization meaning. An authenticated caller on an anonymous
endpoint retains their actor. A named policy deliberately permitting anonymous access sees
the anonymous context. Mapping does not grant permission, change `HttpContext.User`, choose
schemes or turn native challenge/forbid results into library HTTP responses.

The host chooses presentation for `HttpActorResolutionException`; no status/body is built
into the adapter. Consumers validate external identity and decide how to map multiple
authenticated identities, application accounts and attribution. There is no default claim
name, provider lookup, email mapping or inferred initiator.

Use one scoped holder per request and establish before parallel business work. Initialize
it through this adapter rather than pre-initializing it elsewhere in the same request.
Do not mutate the principal in place, rebind identity, or introduce nested authentication
after publication. Principal replacement is detected by reference; claim mutation is not
inspected. Error-route re-execution with a new principal is outside this composition.
Worker and message operations continue using their own core initialization directly.

## Consumer and evidence

[Wholesale HTTP](../../../samples/Wholesale/Api/README.md) demonstrates native cookie/OIDC
configuration, explicit directory mapping and a consumer-owned mapping-failure response.
[Standalone HTTP tests](../tests/ActorIdentityAspNetCoreTests/HttpActorTests.cs) use native
protected cookies and a secondary scheme without sample, EF or tenancy dependencies.
The sample's [browser tests](../../../samples/Wholesale/RuntimeComposition.Tests/BrowserTests.cs)
exercise live OIDC callbacks with their own topology and trust requirements.

## Deferred direction

The adapter establishes canonical actor attribution through native request authentication/
authorization; it does not implement an OIDC client, user directory or session management.
Provider/proxy deployment and principal mutation/re-execution topologies need consumer-specific
proofs. No universal claims mapping, nested context rebinding or transport propagation is
selected. The interface, ordering constraints and limits above are the local supported contract;
repository reports distinguish earlier adapter proofs from later sample browser/provider runs.
