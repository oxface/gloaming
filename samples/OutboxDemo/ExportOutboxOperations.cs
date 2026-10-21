using System.Security.Claims;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ModulithFoundry.Samples.OutboxDemo;

internal static partial class ExportOutboxOperations
{
    internal static void Map(WebApplication app)
    {
        var operations = app.MapGroup("/operations/outbox/dead-letters")
            .RequireAuthorization(ExportOperatorAuthenticationHandler.Permission);
        operations.MapGet("", ListAsync);
        operations.MapGet("/{messageId:guid}", ReadAsync);
        operations.MapPost("/{messageId:guid}/retry", RetryAsync);
    }

    private static async Task<IResult> ListAsync(
        IOutboxDeadLetters<ExportDbContext> deadLetters,
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
                    "exports.render",
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
        IOutboxDeadLetters<ExportDbContext> deadLetters,
        CancellationToken cancellation
    )
    {
        if (messageId == Guid.Empty)
            return Results.BadRequest();

        var state = await deadLetters.GetAsync("exports.render", messageId, cancellation);
        return state is null ? Results.NotFound() : Results.Ok(state);
    }

    internal sealed record RetryRequest(long ExpectedRevision);

    private static async Task<IResult> RetryAsync(
        Guid messageId,
        RetryRequest request,
        ClaimsPrincipal user,
        IOutboxDeadLetters<ExportDbContext> recovery,
        ILoggerFactory logs,
        CancellationToken cancellation
    )
    {
        if (messageId == Guid.Empty || request.ExpectedRevision < 0)
            return Results.BadRequest();

        // Native authorization middleware completed before this endpoint resolves/invokes recovery.
        var result = await recovery.RetryAsync(
            "exports.render",
            messageId,
            request.ExpectedRevision,
            cancellation
        );
        var logger = logs.CreateLogger("Exports.OutboxOperations");
        if (logger.IsEnabled(LogLevel.Information))
        {
            string? operatorId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            OperatorRetry(logger, operatorId, messageId, request.ExpectedRevision, result);
        }

        return result == OutboxRetryResult.Retried ? Results.NoContent() : Results.Conflict();
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Operator {Operator} requested outbox retry for {MessageId} at revision {Revision}: {Result}."
    )]
    private static partial void OperatorRetry(
        ILogger logger,
        string? @operator,
        Guid messageId,
        long revision,
        OutboxRetryResult result
    );
}
