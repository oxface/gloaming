using System.Security.Claims;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.InboxDemo;

internal static partial class RenderInboxOperations
{
    internal static void Map(WebApplication app)
    {
        var operations = app.MapGroup("/operations/inbox")
            .RequireAuthorization(RenderOperatorAuthenticationHandler.Permission);
        operations.MapGet("/{messageId:guid}", ReadAsync);
        operations.MapPost("/{messageId:guid}/retry", RetryAsync);
    }

    private static async Task<IResult> ReadAsync(
        Guid messageId,
        RenderDbContext database,
        CancellationToken cancellation
    )
    {
        var state = await database
            .Set<InboxMessageRecord>()
            .AsNoTracking()
            .Where(row =>
                row.SubscriptionKey == RenderExportHandler.Subscription
                && row.ProducerKey == "exports"
                && row.MessageId == messageId
            )
            .Select(row => new
            {
                row.MessageId,
                row.Revision,
                row.FailureCount,
                row.FailuresSinceRedrive,
                row.ProcessedAt,
                row.QuarantinedAt,
                row.AvailableAt,
                row.LastFailureCode,
                row.LastFailedAt,
                row.RedriveCount,
                row.LastRedrivenAt,
            })
            .SingleOrDefaultAsync(cancellation);
        return state is null ? Results.NotFound() : Results.Ok(state);
    }

    internal sealed record RetryRequest(long ExpectedRevision);

    private static async Task<IResult> RetryAsync(
        Guid messageId,
        RetryRequest request,
        ClaimsPrincipal user,
        IInboxRecovery<RenderDbContext> recovery,
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
