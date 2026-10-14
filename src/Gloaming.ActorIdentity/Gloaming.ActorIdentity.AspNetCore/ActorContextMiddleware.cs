using Microsoft.AspNetCore.Http;

namespace Gloaming.ActorIdentity.AspNetCore;

/// <summary>Completes actor establishment for requests where no policy invoked the evaluator.</summary>
public sealed class ActorContextMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));

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
