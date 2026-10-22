# E3.1 actor identity in native ASP.NET Core requests

Status: owner-reviewed and checkpointed as `faefc0b`, 2026-10-05, following E2.4 checkpoint
`6069c05`. The checkpoint includes the registration helpers requested during owner review;
see [the report](../reports/e3-1-http-actor-identity.md). The original proposal and hypotheses
below explain the scope. Subsequent tenancy work has
[its own interface proposal](e3-2-http-tenancy.md).

## Outcome and extraction hypothesis

An optional `ModulithFoundry.ActorIdentity.AspNetCore` adapter establishes the existing
immutable actor context from the effective native request principal before endpoint work
and authorization handlers that consume it. It references ActorIdentity and the native
`Microsoft.AspNetCore.App` framework; it needs neither Tenancy, EF, Access, OIDC nor Aspire.
ActorIdentity itself remains package-free and unchanged.

The candidate reusable mechanism is correct establishment across native policy-selected
authentication and requests without an authorization policy. Consumer identity mapping,
authentication configuration, authorization requirements and HTTP failure presentation stay
outside it. This is a hypothesis pending executable proofs, not a new reusable guarantee.

## Native ordering findings

Native authorization calls `IPolicyEvaluator.AuthenticateAsync` before evaluating policy
requirements. Explicit policy schemes can replace `HttpContext.User`. Establishment just
after `UseAuthentication()` can therefore capture a different principal from the one used
by authorization. [Native evaluator source, 10.0.12](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Security/Authorization/Policy/src/PolicyEvaluator.cs).

When a policy exists, native middleware authenticates before checking `AllowAnonymous`.
When there is no policy, it continues without calling the evaluator. Both paths need
establishment. An authorization requirement alone cannot cover anonymous endpoints or
guarantee establishment before other handlers. A result-handler-only adapter runs too late
for those handlers. [Native middleware source, 10.0.12](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Security/Authorization/Policy/src/AuthorizationMiddleware.cs).

Propose an explicit evaluator wrapper that establishes after native policy authentication,
plus downstream middleware that completes establishment when no policy invoked the wrapper.
Delegate policy selection, authentication, requirements, authorization results and challenges
to the existing native mechanisms. Do not compute policies twice, run authentication a
second time for mapping, add an actor requirement to every policy, or replace authorization
middleware. [Native default/fallback policy rules](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0).

These are source findings for the installed 10.0.12 framework, not executed adapter proofs.

## Proposed public interface

One consumer resolver, an evaluator wrapper, middleware and one typed mapping failure:

```csharp
public interface IHttpActorContextResolver
{
    ValueTask<ActorContext?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

public sealed class ActorContextPolicyEvaluator : IPolicyEvaluator
{
    public ActorContextPolicyEvaluator(
        IPolicyEvaluator inner,
        IHttpActorContextResolver resolver,
        IActorContextInitializer initializer);

    // Implements the two native IPolicyEvaluator methods without new policy types.
}

public sealed class ActorContextMiddleware
{
    public ActorContextMiddleware(RequestDelegate next);

    public Task InvokeAsync(
        HttpContext context,
        IHttpActorContextResolver resolver,
        IActorContextInitializer initializer);
}

public sealed class HttpActorResolutionException : Exception
{
    public HttpActorResolutionException(string message);
}

public static class HttpActorContextExtensions
{
    public static IServiceCollection AddHttpActorContext<TResolver>(
        this IServiceCollection services)
        where TResolver : class, IHttpActorContextResolver;

    public static IServiceCollection AddHttpActorContext<TResolver>(
        this IServiceCollection services,
        Func<IServiceProvider, IPolicyEvaluator> innerEvaluatorFactory)
        where TResolver : class, IHttpActorContextResolver;

    public static IApplicationBuilder UseHttpActorContext(this IApplicationBuilder app);
}
```

The resolver runs only when the effective principal contains an authenticated identity.
It maps trusted native identity into an application actor and may explicitly provide an
initiator. It receives the effective `HttpContext.User`, scoped services through its own
constructor, and `RequestAborted`. It performs no authentication or principal mutation.
Returning null means an authenticated identity could not be mapped. Returning an anonymous
actor for an authenticated principal is also a mapping failure. The adapter throws
`HttpActorResolutionException` for both; it never silently downgrades them to anonymity.
Unexpected resolver faults and cancellation propagate without retry or translation.

When no identity is authenticated, the adapter explicitly establishes
`new ActorContext(Actor.Anonymous)` without calling the resolver or inferring an initiator.
This does not deny access: native policy decides whether the endpoint accepts the caller.
On an anonymous endpoint, an authenticated caller retains their mapped actor. Consumer
resolvers decide how to handle multiple authenticated identities; the adapter must not
choose the first identity or claim as an application user by convention.

The wrapper delegates authentication to `inner`, establishes from the resulting principal,
and returns the original authentication result. Its authorization method delegates unchanged
to `inner`. Native middleware and result handling remain responsible for policy enforcement,
scheme-specific challenges and forbids. The adapter assigns no HTTP status or response body.

Both entry points share an internal request feature recording successful establishment and
the principal used. Downstream middleware skips completed establishment. Publication occurs
only after resolution and core initialization succeed. Failed establishment stops the
pipeline without publishing partial context. No core reset or replacement is introduced.
Repeated processing with a different principal fails rather than reusing a stale actor.
In-place principal mutation, nested authentication and error-route re-execution that changes
identity are outside this first composition; use a separate operation scope when needed.

## Explicit consumer recipe

Consumers first register native authentication/authorization, then explicitly opt into the
scoped holder, its two aliases, a scoped resolver and a transient wrapped native evaluator:

```csharp
services.AddHttpActorContext<ApplicationActorResolver>();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpActorContext();
```

Call each helper once. Registration supplies its own actor holder, resolver and evaluator;
these become the native DI single-service choices over earlier registrations. Consumers using
a custom evaluator select it explicitly with the factory overload:

```csharp
services.AddTransient<CustomPolicyEvaluator>();
services.AddHttpActorContext<ApplicationActorResolver>(provider =>
    provider.GetRequiredService<CustomPolicyEvaluator>());
```

The factory must not resolve `IPolicyEvaluator`, which would recursively request the wrapper.
Registration does not search, capture or silently decorate arbitrary existing descriptors.
Direct holder/wrapper/middleware wiring remains available for alternate service construction.
The helpers add no authentication schemes, policies, native authorization registration, failure
response or automatic middleware ordering. No Scrutor dependency is introduced.
Both integration points are required to cover policy and policy-free requests.
Authentication handlers cannot read actor context
before policy authentication has finished. Business handlers inject the existing accessor.

The host's outer exception handling chooses mapping-failure presentation. The sample chooses
403 with a generic failure body for an authenticated but unmapped identity; this is sample
policy, not an adapter default. It writes the error response without re-executing routed
authentication. Missing credentials continue through native authorization/challenge behavior.

## Executable consumer and template material

Add a small `samples/Wholesale/Api` host without tenancy, EF or messaging. It
uses editable native cookie/OIDC registration and configuration, a configured directory of
known validated issuer/subject pairs mapped to stable application-user keys, and the recipe
above. That directory is a consumer-owned fixture, not the later durable Access model.
There is no automatic user creation, email matching, account linking or caller-selected actor.

The host demonstrates a native login challenge, anonymous health, an authenticated identity
read and a public identity read that retains authenticated identity. Provider configuration
comes from consumer configuration; no development header/sign-in shortcut ships in the
runnable host. OIDC callback handling stays in the native authentication handler.
No logout, business mutation or claim of complete BFF/session security is introduced here.

The host may require an operator-supplied OIDC provider to exercise login. Container-free
HTTP proofs use real native protected cookie tickets; additional authentication schemes are
test-only instruments. Test setup must not become a production authentication implementation.
Actual provider login/callback/session/antiforgery proofs belong to the later E3 topology
increment and remain unproven by this slice. Do not require personal provider credentials
for CI. Native OpenIdConnect is a sample-only package; TestHost is test-only, centrally pinned
to the active 10.0.12 framework. Neither enters the adapter or either foundation core.

Template output is the executable registration/pipeline and native authentication recipe,
with consumer directory/failure policy visible for editing. No separate generated template
or CLI is needed yet. Consumer-owned Access will later replace fixture identity resolution.

## Focused proof matrix

Tests call HTTP endpoints through the actual adapter and native request pipeline. Assert
observable actor identity and relevant native response behavior together, avoiding independent
tests that merely establish that authorization/cookies/DI work.

| Case | Required evidence |
| --- | --- |
| Native default and fallback | Valid cookie resolves to the configured application actor; absent credentials retain native challenge behavior, and business work does not execute. |
| Anonymous and permissive policy | Without credentials, both an anonymous endpoint and a named policy deliberately allowing anonymous execution observe the explicit anonymous actor. |
| Retained identity | The public endpoint with a valid cookie observes the authenticated actor, including the policy-free path with no fallback policy. |
| Effective scheme | Different default and endpoint-selected identities yield the endpoint-selected actor in both a consumer authorization handler and the endpoint; test Minimal API policy metadata and a routed `[Authorize(AuthenticationSchemes = ...)]` endpoint. |
| Selected-scheme failure | Failure of the selected scheme must not retain the default scheme's mapped actor; native challenge is preserved for a protected endpoint. |
| Mapping failure | An authenticated but unmapped identity cannot invoke a public or protected endpoint; a resolver returning anonymous fails as well. The host controls the failure response. |
| Native permission denial | A resolved actor is available inside a consumer policy handler, but failed permission produces native forbid and no business invocation. Mapping grants no permission. |
| Scope isolation | Overlapping requests for two users and a following anonymous request retain independently expected contexts; count mapping calls to detect double establishment. |
| Cancellation and recovery | Cancel an awaiting resolver; no endpoint or partial context is published. A fresh request resolves successfully without a retry in the failed request. |
| Explicit composition | A consumer-owned inner evaluator is delegated to, policy-free establishment works, and a changed principal cannot silently reuse an established actor. |
| Independent adoption | Adapter and host build/use ActorIdentity without Tenancy, EF, sample Access or transports; the package-free core dependency policy still holds. |

Cover both policy-backed and policy-free paths; use representative native cookie and
test-only scheme configurations rather than a Cartesian product of native metadata. Extend
active architecture declarations for the new adapter's direct core/framework dependencies.
Add the HTTP suite to the container-free CI lane and hook coverage; leave existing PostgreSQL
proofs intact. Final counts and guarantees come from actual implementation executions.

## Exit, review and subsequent slices

Review the resolver/failure contract and explicit two-part pipeline first, then implement
one coherent runnable slice with its focused HTTP proofs. Reassess extraction if consumer
configuration exceeds the behavior this adapter removes. No new reusable mechanism is proven
by this proposal, and no ADR freezes these public types before owner review.

Next, plan the independently adoptable Tenancy HTTP adapter and configurable selection
utilities, then consumer-owned Access membership/admission. Persisted external identity,
membership revocation, invitations, roles, tenant-bearing business endpoints, real provider
topology and full BFF security receive separate increments. E2's initial supported persistence
scope is complete; shared cross-module transactions still need their own named workflow.
