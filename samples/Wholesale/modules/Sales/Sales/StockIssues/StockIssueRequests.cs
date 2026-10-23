using System.Diagnostics;
using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Sales.StockIssues;

internal sealed class StockIssueRequests(
    SalesDbContext database,
    IActorContextAccessor actor,
    IOutbox<SalesDbContext> outbox
) : IStockIssueRequests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    public async Task<StockIssueRequest> StartAsync(
        StartStockIssueRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfEqual(request.RequestId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(request.StockPositionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.ExpectedStockVersion, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(request.ExpectedStockVersion, long.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.Quantity, 0);
        ArgumentOutOfRangeException.ThrowIfEqual(request.ReplyDeadline, default);
        string organization = database.RequiredOrganizationKey;
        if (organization is not ("wholesale-alpha" or "wholesale-beta"))
            throw new InvalidOperationException(
                "The stock-issue sample requires an admitted Organization."
            );

        if (actor.Current.Actor.Kind == ActorKind.Anonymous)
            throw new InvalidOperationException(
                "Starting a stock-issue request requires an established actor."
            );

        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var existing = await database
            .Set<StockIssueRequestRow>()
            .SingleOrDefaultAsync(row => row.Id == request.RequestId, cancellationToken);
        if (existing is not null)
        {
            existing.ValidateRetry(request);
            return existing.Read();
        }

        var progress = StockIssueRequestRow.Create(organization, request);
        database.Add(progress);
        outbox.Enqueue(
            OutgoingMessage.FromPayload(
                progress.CommandMessageId,
                "inventory.issue-stock",
                "inventory.issue-stock",
                1,
                new IssueStockV1(
                    request.StockPositionId,
                    request.ExpectedStockVersion,
                    request.Quantity
                ),
                WireJson,
                organization,
                request.RequestId.ToString("D"),
                traceParent: Activity.Current?.Id,
                traceState: Activity.Current?.TraceStateString
            )
        );
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return progress.Read();
    }

    public async Task<StockIssueRequest?> ReadAsync(
        Guid requestId,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        _ = database.RequiredOrganizationKey;

        var row = await database
            .Set<StockIssueRequestRow>()
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == requestId, cancellationToken);
        return row?.Read();
    }
}
