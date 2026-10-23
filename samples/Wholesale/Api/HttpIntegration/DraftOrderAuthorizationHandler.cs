using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Gloaming.Samples.Wholesale.Api.HttpIntegration;

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
