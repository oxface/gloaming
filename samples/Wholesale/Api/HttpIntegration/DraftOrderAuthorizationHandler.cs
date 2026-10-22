using Microsoft.AspNetCore.Authorization;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Api.HttpIntegration;

internal sealed record DraftOrderRequirement(DraftOrderAction Permission)
    : IAuthorizationRequirement;

internal sealed record DraftOrderResource(Guid Id, CancellationToken CancellationToken);

internal sealed class DraftOrderAuthorizationHandler(IDraftOrderAuthorization authorization)
    : AuthorizationHandler<DraftOrderRequirement, DraftOrderResource>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DraftOrderRequirement requirement,
        DraftOrderResource resource
    )
    {
        if (
            await authorization.IsAllowedAsync(
                resource.Id,
                requirement.Permission,
                resource.CancellationToken
            )
        )
            context.Succeed(requirement);
    }
}
