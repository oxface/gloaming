using Microsoft.AspNetCore.Http;

namespace Gloaming.ActorIdentity.AspNetCore;

/// <summary>Completes actor establishment for requests where no policy invoked the evaluator.</summary>
public sealed class ActorContextMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));

    /// <summary>Completes actor context on requests not already initialized by native policy evaluation, then invokes the next middleware.</summary>
    public async Task InvokeAsync(
        HttpContext context,
        IHttpActorContextResolver resolver,
        IActorContextInitializer initializer
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(initializer);
        await HttpActorContextEstablishment.EnsureInitializedAsync(context, resolver, initializer);
        await _next(context);
    }
}
