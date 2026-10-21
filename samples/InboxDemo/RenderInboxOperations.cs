using System.Security.Claims;
using Gloaming.Messaging.EntityFrameworkCore;

namespace ModulithFoundry.Samples.InboxDemo;

internal static partial class RenderInboxOperations
{
    internal static void Map(WebApplication app)
    {
        var operations = app.MapGroup("/operations/inbox/dead-letters")
            .RequireAuthorization(RenderOperatorAuthenticationHandler.Permission);
        operations.MapGet("", ListAsync);
        operations.MapGet("/{messageId:guid}", ReadAsync);
        operations.MapPost("/{messageId:guid}/retry", RetryAsync);
    }

    private static async Task<IResult> ListAsync(
        IInboxDeadLetters<RenderDbContext> deadLetters,
        int? pageSize,
        string? failureCode,
        string? after,
        CancellationToken cancellation
    )
    {
        try
        {
            return Results.Ok(
                await deadLetters.ListAsync(
                    RenderExportHandler.Subscription,
                    "exports",
                    new(pageSize ?? 50, failureCode, after),
                    cancellation
                )
            );
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }
    }

    private static async Task<IResult> ReadAsync(
        Guid messageId,
        IInboxDeadLetters<RenderDbContext> deadLetters,
        CancellationToken cancellation
    )
    {
        if (messageId == Guid.Empty)
            return Results.BadRequest();

        var state = await deadLetters.GetAsync(
            RenderExportHandler.Subscription,
            "exports",
            messageId,
            cancellation
        );
        return state is null ? Results.NotFound() : Results.Ok(state);
    }

    internal sealed record RetryRequest(long ExpectedRevision);

    private static async Task<IResult> RetryAsync(
        Guid messageId,
        RetryRequest request,
        ClaimsPrincipal user,
        IInboxDeadLetters<RenderDbContext> recovery,
        ILoggerFactory logs,
        CancellationToken cancellation
    )
    {
        if (messageId == Guid.Empty || request.ExpectedRevision < 0)
            return Results.BadRequest();

        // Native authorization middleware completed before this endpoint resolves/invokes recovery.
        var result = await recovery.RetryAsync(
            RenderExportHandler.Subscription,
            "exports",
            messageId,
            request.ExpectedRevision,
            cancellation
        );
        var logger = logs.CreateLogger("Rendering.InboxOperations");
        if (logger.IsEnabled(LogLevel.Information))
        {
            string? operatorId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            OperatorRetry(logger, operatorId, messageId, request.ExpectedRevision, result);
        }

        return result == InboxRetryResult.Retried ? Results.NoContent() : Results.Conflict();
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Operator {Operator} requested inbox retry for {MessageId} at revision {Revision}: {Result}."
    )]
    private static partial void OperatorRetry(
        ILogger logger,
        string? @operator,
        Guid messageId,
        long revision,
        InboxRetryResult result
    );
}
