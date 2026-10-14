using Microsoft.AspNetCore.Http;

namespace Gloaming.ActorIdentity.AspNetCore;

/// <summary>Consumer mapping from the effective authenticated principal into application attribution.</summary>
public interface IHttpActorContextResolver
{
    /// <summary>Returns null when the authenticated principal cannot be mapped.</summary>
    /// <remarks>
    /// Called only for an authenticated effective principal. Do not modify HttpContext.User.
    /// Return an identified actor and explicitly supplied attribution; returning anonymity
    /// is a mapping failure. The host chooses failure responses and owns the request scope.
    /// </remarks>
    ValueTask<ActorContext?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    );
}
