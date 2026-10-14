using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Gloaming.ActorIdentity.AspNetCore;

internal static class HttpActorContextEstablishment
{
    internal static async ValueTask EnsureInitializedAsync(
        HttpContext context,
        IHttpActorContextResolver resolver,
        IActorContextInitializer initializer
    )
    {
        CancellationToken cancellation = context.RequestAborted;
        cancellation.ThrowIfCancellationRequested();
        ClaimsPrincipal principal = context.User;
        if (context.Features.Get<EstablishedActor>() is { } established)
        {
            if (!ReferenceEquals(established.Principal, principal))
                throw new InvalidOperationException(
                    "An established actor cannot be reused with a different request principal."
                );
            return;
        }

        bool authenticated = principal.Identities.Any(identity => identity.IsAuthenticated);
        ActorContext? actor = authenticated
            ? await resolver.ResolveAsync(context, cancellation)
            : new ActorContext(Actor.Anonymous);
        cancellation.ThrowIfCancellationRequested();
        if (actor is null || (authenticated && actor.Actor.Kind == ActorKind.Anonymous))
            throw new HttpActorResolutionException(
                "The authenticated principal could not be mapped to an identified application actor."
            );
        if (!ReferenceEquals(context.User, principal))
            throw new InvalidOperationException(
                "Actor resolution must not replace the request principal."
            );

        initializer.Initialize(actor);
        context.Features.Set(new EstablishedActor(principal));
    }

    private sealed record EstablishedActor(ClaimsPrincipal Principal);
}
