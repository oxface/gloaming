using Gloaming.Samples.Wholesale.Api.HttpIntegration;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gloaming.Samples.Wholesale.Api.Endpoints;

public static class DraftOrderEndpoints
{
    public static void Map(
        IEndpointRouteBuilder endpoints,
        string pattern,
        bool allowLifecycle = true
    )
    {
        endpoints.MapGet(pattern, SearchAsync).RequireAuthorization();
        endpoints.MapGet(pattern + "/{draftId:guid}", ReadAsync).RequireAuthorization();
        if (allowLifecycle)
        {
            endpoints
                .MapPost(pattern, CreateAsync)
                .RequireAuthorization()
                .AddEndpointFilter<ValidateAntiforgeryFilter>();
            endpoints
                .MapPost(pattern + "/{draftId:guid}/submit", SubmitAsync)
                .RequireAuthorization()
                .AddEndpointFilter<ValidateAntiforgeryFilter>();
        }

        endpoints
            .MapPut(pattern + "/{draftId:guid}/lines", ChangeLinesAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidateAntiforgeryFilter>();
        endpoints
            .MapPut(pattern + "/{draftId:guid}/line", ChangeAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidateAntiforgeryFilter>();
    }

    private static async Task<IResult> CreateAsync(
        CreateDraftOrder request,
        DraftOrderEdits edits,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Result(await edits.CreateAsync(request, cancellationToken));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    }

    private static async Task<IResult> SubmitAsync(
        Guid draftId,
        SubmitDraft edit,
        HttpContext http,
        IAuthorizationService authorization,
        DraftOrderEdits edits,
        SalesReadTransaction transactions,
        CancellationToken cancellationToken
    )
    {
        if (
            draftId == Guid.Empty
            || edit.ExpectedVersion < 1
            || edit.ExpectedVersion == long.MaxValue
            || edit.OperationId == Guid.Empty
        )
            return Results.BadRequest();

        await using (var transaction = await transactions.BeginAsync(cancellationToken))
        {
            var decision = await authorization.AuthorizeAsync(
                http.User,
                new DraftOrderResource(draftId, cancellationToken),
                [new DraftOrderRequirement(DraftOrderAction.Edit)]
            );
            if (!decision.Succeeded)
                return Results.NotFound();
        }

        return Result(
            await edits.SubmitAsync(
                new(draftId, edit.ExpectedVersion, edit.OperationId),
                cancellationToken
            )
        );
    }

    private static async Task<IResult> ChangeLinesAsync(
        Guid draftId,
        DraftLinesEdit edit,
        HttpContext http,
        IAuthorizationService authorization,
        DraftOrderEdits edits,
        SalesReadTransaction transactions,
        CancellationToken cancellationToken
    )
    {
        if (
            draftId == Guid.Empty
            || edit.ExpectedVersion < 1
            || edit.ExpectedVersion == long.MaxValue
            || edit.Add is null
            || edit.Change is null
            || edit.Remove is null
        )
            return Results.BadRequest();

        await using (var transaction = await transactions.BeginAsync(cancellationToken))
        {
            var decision = await authorization.AuthorizeAsync(
                http.User,
                new DraftOrderResource(draftId, cancellationToken),
                [new DraftOrderRequirement(DraftOrderAction.Edit)]
            );
            if (!decision.Succeeded)
                return Results.NotFound();
        }

        try
        {
            DraftOrderLineChange[] changes =
            [
                .. edit.Add.Select(line =>
                    (DraftOrderLineChange)new DraftOrderLineChange.Add(line)
                ),
                .. edit.Change.Select(line => new DraftOrderLineChange.Change(
                    line.LineId,
                    line.Quantity,
                    line.UnitPrice
                )),
                .. edit.Remove.Select(id => new DraftOrderLineChange.Remove(id)),
            ];
            return Result(
                await edits.ChangeAsync(
                    new(draftId, edit.ExpectedVersion, changes),
                    cancellationToken
                )
            );
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    }

    private static IResult Result(DraftOrderChangeResult result) =>
        result switch
        {
            DraftOrderChangeResult.Changed changed => Results.Ok(changed.Proposed),
            DraftOrderChangeResult.NotFound => Results.NotFound(),
            DraftOrderChangeResult.CustomerNotFound or DraftOrderChangeResult.UnknownSku =>
                Results.BadRequest(),
            DraftOrderChangeResult.Conflict
            or DraftOrderChangeResult.AlreadySubmitted
            or DraftOrderChangeResult.LimitExceeded => Results.Conflict(),
            _ => throw new InvalidOperationException("Unexpected draft operation outcome."),
        };

    private static async Task<Results<Ok<DraftOrderPage>, BadRequest>> SearchAsync(
        IDraftOrderQueries queries,
        SalesReadTransaction transactions,
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
            await using var transaction = await transactions.BeginAsync(cancellationToken);
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
        SalesReadTransaction transactions,
        CancellationToken cancellationToken
    )
    {
        if (draftId == Guid.Empty)
            return TypedResults.BadRequest();

        await using var transaction = await transactions.BeginAsync(cancellationToken);
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
        SalesReadTransaction transactions,
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

        bool allowed;
        await using (var transaction = await transactions.BeginAsync(cancellationToken))
        {
            var decision = await authorization.AuthorizeAsync(
                http.User,
                new DraftOrderResource(draftId, cancellationToken),
                [new DraftOrderRequirement(DraftOrderAction.Edit)]
            );
            allowed = decision.Succeeded;
        }

        if (!allowed)
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

public sealed record SubmitDraft(long ExpectedVersion, Guid? OperationId = null);

public sealed record DraftLinesEdit(
    long ExpectedVersion,
    DraftOrderLine[] Add,
    DraftOrderLineEdit[] Change,
    Guid[] Remove
);
