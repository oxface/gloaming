using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints;

public static class DraftOrderEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints, string pattern)
    {
        endpoints.MapGet(pattern, SearchAsync).RequireAuthorization();
        endpoints.MapGet(pattern + "/{draftId:guid}", ReadAsync).RequireAuthorization();
        endpoints
            .MapPut(pattern + "/{draftId:guid}/line", ChangeAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidateAntiforgeryFilter>();
    }

    private static async Task<Results<Ok<DraftOrderPage>, BadRequest>> SearchAsync(
        IDraftOrderQueries queries,
        string? customerNameContains,
        string? currency,
        decimal? minimumTotal,
        int? offset,
        int? limit,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var page = await queries.SearchAsync(
                new(customerNameContains, currency, minimumTotal, offset ?? 0, limit ?? 20),
                cancellationToken
            );
            return TypedResults.Ok(page);
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest();
        }
    }

    private static async Task<Results<Ok<DraftOrder>, NotFound, BadRequest>> ReadAsync(
        Guid draftId,
        HttpContext http,
        IAuthorizationService authorization,
        IDraftOrderQueries queries,
        CancellationToken cancellationToken
    )
    {
        if (draftId == Guid.Empty)
            return TypedResults.BadRequest();

        var decision = await authorization.AuthorizeAsync(
            http.User,
            new DraftOrderResource(draftId, cancellationToken),
            [new DraftOrderRequirement(DraftOrderAction.Read)]
        );
        if (!decision.Succeeded)
            return TypedResults.NotFound();

        var draft = await queries.ReadAsync(draftId, cancellationToken);
        return draft is null ? TypedResults.NotFound() : TypedResults.Ok(draft);
    }

    private static async Task<Results<Ok<DraftOrder>, NotFound, BadRequest, Conflict>> ChangeAsync(
        Guid draftId,
        DraftOrderLineEdit edit,
        HttpContext http,
        IAuthorizationService authorization,
        DraftOrderEdits edits,
        CancellationToken cancellationToken
    )
    {
        if (
            draftId == Guid.Empty
            || edit.LineId == Guid.Empty
            || edit.ExpectedVersion < 1
            || edit.ExpectedVersion == long.MaxValue
            || edit.Quantity <= 0
            || edit.UnitPrice < 0
        )
            return TypedResults.BadRequest();

        var decision = await authorization.AuthorizeAsync(
            http.User,
            new DraftOrderResource(draftId, cancellationToken),
            [new DraftOrderRequirement(DraftOrderAction.Edit)]
        );
        if (!decision.Succeeded)
            return TypedResults.NotFound();

        DraftOrderChangeResult result;
        try
        {
            result = await edits.ChangeAsync(
                new(
                    draftId,
                    edit.ExpectedVersion,
                    [new DraftOrderLineChange.Change(edit.LineId, edit.Quantity, edit.UnitPrice)]
                ),
                cancellationToken
            );
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest();
        }

        return result switch
        {
            DraftOrderChangeResult.Changed changed => TypedResults.Ok(changed.Proposed),
            DraftOrderChangeResult.NotFound => TypedResults.NotFound(),
            DraftOrderChangeResult.Conflict
            or DraftOrderChangeResult.AlreadySubmitted
            or DraftOrderChangeResult.LimitExceeded => TypedResults.Conflict(),
            _ => throw new InvalidOperationException("Unexpected draft edit outcome."),
        };
    }
}

public sealed record DraftOrderLineEdit(
    long ExpectedVersion,
    Guid LineId,
    decimal Quantity,
    decimal UnitPrice
);
