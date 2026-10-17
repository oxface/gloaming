using Microsoft.AspNetCore.Http.HttpResults;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints;

public static class CustomerProfileEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints, string pattern)
    {
        endpoints.MapGet(pattern, ReadAsync).RequireAuthorization();
        endpoints
            .MapPut(pattern, ChangeAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidateAntiforgeryFilter>();
    }

    private static async Task<Results<Ok<CustomerProfile>, NotFound, ProblemHttpResult>> ReadAsync(
        Guid customerId,
        ICustomerProfiles profiles,
        SalesReadTransaction transactions,
        CancellationToken cancellation
    )
    {
        if (customerId == Guid.Empty)
            return TypedResults.Problem(statusCode: 400, title: "Invalid customer ID.");

        await using var transaction = await transactions.BeginAsync(cancellation);
        CustomerProfile? profile = await profiles.ReadAsync(customerId, cancellation);
        return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
    }

    private static async Task<IResult> ChangeAsync(
        Guid customerId,
        ProfileEdit edit,
        ICustomerProfiles profiles,
        CancellationToken cancellation
    )
    {
        if (
            customerId == Guid.Empty
            || edit.AddressId == Guid.Empty
            || edit.ExpectedVersion < 1
            || edit.ExpectedVersion == long.MaxValue
            || string.IsNullOrWhiteSpace(edit.DisplayName)
            || edit.DisplayName.Length > 256
            || string.IsNullOrWhiteSpace(edit.AddressLine)
            || edit.AddressLine.Length > 512
        )
            return Results.Problem(statusCode: 400, title: "Invalid profile change.");
        ProfileChangeResult result = await profiles.ChangeAsync(
            new CustomerProfileChange(
                customerId,
                edit.AddressId,
                edit.ExpectedVersion,
                edit.DisplayName,
                edit.AddressLine
            ),
            cancellation
        );
        return result switch
        {
            ProfileChangeResult.Updated updated => Results.Ok(updated.Profile),
            ProfileChangeResult.NotFound => Results.NotFound(),
            ProfileChangeResult.Conflict => Results.Conflict(),
            _ => throw new InvalidOperationException("Unknown profile change outcome."),
        };
    }
}

public sealed record ProfileEdit(
    Guid AddressId,
    long ExpectedVersion,
    string DisplayName,
    string AddressLine
);
