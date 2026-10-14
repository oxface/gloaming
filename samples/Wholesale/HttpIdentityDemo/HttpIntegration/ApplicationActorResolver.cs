using System.Security.Claims;
using Gloaming.ActorIdentity;
using Gloaming.ActorIdentity.AspNetCore;
using ModulithFoundry.Samples.Wholesale.Access.Contracts;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;

internal sealed class ApplicationActorResolver(IApplicationAccess access)
    : IHttpActorContextResolver
{
    public async ValueTask<ActorContext?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClaimsIdentity[] identities = httpContext
            .User.Identities.Where(identity => identity.IsAuthenticated)
            .ToArray();
        // This consumer admits exactly one authenticated identity with one issuer/subject pair.
        if (identities.Length != 1)
            return null;
        Claim[] issuers = identities[0].FindAll("iss").ToArray();
        Claim[] subjects = identities[0].FindAll("sub").ToArray();
        if (
            issuers.Length != 1
            || subjects.Length != 1
            || string.IsNullOrWhiteSpace(issuers[0].Value)
            || string.IsNullOrWhiteSpace(subjects[0].Value)
        )
            return null;
        var identity = new ExternalIdentity(issuers[0].Value, subjects[0].Value);
        UserId? user = await access.ResolveUserAsync(identity, cancellationToken);
        return user is null ? null : new ActorContext(Actor.Human(new ActorId(user.Value)));
    }
}
