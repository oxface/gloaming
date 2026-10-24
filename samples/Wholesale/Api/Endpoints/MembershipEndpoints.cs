using Gloaming.Samples.Wholesale.Access.Contracts;
using Gloaming.Samples.Wholesale.Api.HttpIntegration;

namespace Gloaming.Samples.Wholesale.Api.Endpoints;

public static class MembershipEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints, string pattern)
    {
        endpoints.MapGet(pattern, ReadAsync).RequireAuthorization();
        endpoints
            .MapPost(pattern + "/suspend", SuspendAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidateAntiforgeryFilter>();
        endpoints
            .MapPost(pattern + "/reactivate", ReactivateAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidateAntiforgeryFilter>();
    }

    private static async Task<IResult> ReadAsync(
        Guid membershipId,
        IMembershipAdministration administration,
        CancellationToken cancellationToken
    )
    {
        if (membershipId == Guid.Empty)
            return Results.BadRequest();

        OrganizationMembership? result = await administration.ReadAsync(
            membershipId,
            cancellationToken
        );
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static Task<IResult> SuspendAsync(
        Guid membershipId,
        MembershipEdit edit,
        IMembershipAdministration administration,
        CancellationToken cancellationToken
    ) => ChangeAsync(membershipId, edit, administration.SuspendAsync, cancellationToken);

    private static Task<IResult> ReactivateAsync(
        Guid membershipId,
        MembershipEdit edit,
        IMembershipAdministration administration,
        CancellationToken cancellationToken
    ) => ChangeAsync(membershipId, edit, administration.ReactivateAsync, cancellationToken);

    private static async Task<IResult> ChangeAsync(
        Guid membershipId,
        MembershipEdit edit,
        Func<MembershipChange, CancellationToken, Task<MembershipChangeResult>> change,
        CancellationToken cancellationToken
    )
    {
        if (
            membershipId == Guid.Empty
            || edit.ExpectedRevision < 1
            || edit.ExpectedRevision == long.MaxValue
        )
            return Results.BadRequest();

        var result = await change(
            new MembershipChange(membershipId, edit.ExpectedRevision),
            cancellationToken
        );
        return result switch
        {
            MembershipChangeResult.Changed changed => Results.Ok(changed.Membership),
            MembershipChangeResult.AlreadyInRequestedState unchanged => Results.Ok(
                unchanged.Membership
            ),
            MembershipChangeResult.NotFound => Results.NotFound(),
            MembershipChangeResult.Conflict => Results.Conflict(),
            MembershipChangeResult.InvalidTransition => Results.Problem(
                statusCode: 409,
                title: "Membership transition is not allowed."
            ),
            MembershipChangeResult.SelfSuspensionNotAllowed => Results.Problem(
                statusCode: 409,
                title: "Self-suspension is not allowed."
            ),
            MembershipChangeResult.LastAdministrator => Results.Problem(
                statusCode: 409,
                title: "Keep an active membership administrator."
            ),
            _ => throw new InvalidOperationException("Unknown membership change outcome."),
        };
    }
}

public sealed record MembershipEdit(long ExpectedRevision);
