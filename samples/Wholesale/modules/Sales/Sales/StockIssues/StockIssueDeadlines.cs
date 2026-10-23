using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Sales;

/// <summary>Separate Sales maintenance lane; expiry flags uncertainty and never reverses Inventory effects.</summary>
public sealed class StockIssueDeadlines(SalesDbContext database, TimeProvider clock)
{
    /// <summary>Commits a bounded overdue scan in the established Organization. Retry conflicts in a fresh scope.</summary>
    public async Task<int> MarkOverdueAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, 64);
        _ = database.RequiredOrganizationKey;

        var now = clock.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var requests = await database
            .Set<StockIssues.StockIssueRequestRow>()
            .Where(row =>
                row.Status == StockIssueRequestStatus.AwaitingReply && row.ReplyDeadline <= now
            )
            .OrderBy(row => row.ReplyDeadline)
            .ThenBy(row => row.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        foreach (var request in requests)
            request.MarkOverdue();

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return requests.Length;
    }
}
