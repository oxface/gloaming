using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Gloaming.ActorIdentity.AspNetCore;

/// <summary>Establishes actor context after native policy authentication and before authorization.</summary>
public sealed class ActorContextPolicyEvaluator(
    IPolicyEvaluator inner,
    IHttpActorContextResolver resolver,
    IActorContextInitializer initializer
) : IPolicyEvaluator
{
    private readonly IPolicyEvaluator _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IHttpActorContextResolver _resolver =
        resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly IActorContextInitializer _initializer =
        initializer ?? throw new ArgumentNullException(nameof(initializer));

    public async Task<AuthenticateResult> AuthenticateAsync(
        AuthorizationPolicy policy,
        HttpContext context
    )
    {
        AuthenticateResult result = await _inner.AuthenticateAsync(policy, context);
        await HttpActorContextEstablishment.EnsureInitializedAsync(
            context,
            _resolver,
            _initializer
        );
        return result;
    }

    public Task<PolicyAuthorizationResult> AuthorizeAsync(
        AuthorizationPolicy policy,
        AuthenticateResult authenticationResult,
        HttpContext context,
        object? resource
    ) => _inner.AuthorizeAsync(policy, authenticationResult, context, resource);
}
