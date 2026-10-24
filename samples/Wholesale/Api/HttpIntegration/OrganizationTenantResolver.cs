using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Access.Contracts;
using Gloaming.Tenancy;
using Gloaming.Tenancy.AspNetCore;

namespace Gloaming.Samples.Wholesale.Api.HttpIntegration;

internal sealed class OrganizationTenantResolver(
    IApplicationAccess access,
    IActorContextAccessor actor
) : IHttpTenantCandidateResolver
{
    public async ValueTask<TenantContext?> ResolveAsync(
        string? candidate,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidate is null)
            return TenantContext.Tenantless();
        OrganizationId? organization;
        if (
            httpContext.GetEndpoint()?.Metadata.GetMetadata<PublicOrganizationAccessAttribute>()
            is not null
        )
            organization = await access.ResolvePublicOrganizationAsync(
                candidate,
                cancellationToken
            );
        else
        {
            Actor current = actor.Current.Actor;
            if (current.Kind != ActorKind.Human)
                return null;
            organization = await access.ResolveMemberOrganizationAsync(
                new UserId(current.Id!.Value),
                candidate,
                cancellationToken
            );
        }
        return organization is null
            ? null
            : TenantContext.ForTenant(new TenantId(organization.Value));
    }
}
