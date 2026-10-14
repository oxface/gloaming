using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.ProfileFeed;

namespace ModulithFoundry.Samples.Wholesale.Sales;

/// <summary>Explicit module composition/maintenance, never application startup or public ingress.</summary>
public static class CustomerProfileFeedSetup
{
    /// <summary>Activates the admitted Organization in the caller's native transaction; never saves, commits or resets existing progress.</summary>
    public static async Task ActivateAsync(
        SalesDbContext database,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(database);

        FeedStorage.RequireWriterTransaction(database);
        string organization = database.RequiredOrganizationKey;
        Guid generation = Guid.NewGuid();
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO sales.customer_profile_feed_state (organization_key, generation, head, floor)
            VALUES ({organization}, {generation}, 0, 0) ON CONFLICT (organization_key) DO NOTHING
            """,
            cancellationToken
        );
    }

    /// <summary>Atomically discards a retained prefix and advances its floor. Caller supplies a fresh admitted context.</summary>
    public static async Task PruneThroughAsync(
        SalesDbContext database,
        CustomerProfileFeedCursor through,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        FeedStorage.ValidateCursor(through);

        FeedStorage.RequireIndependent(database);
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        await FeedStorage.LockAsync(database, cancellationToken);
        var state = await database
            .Set<FeedStateRow>()
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        if (state.Generation != through.Generation || through.AfterPosition > state.Head)
            throw new ArgumentException(
                "Retention cutoff does not belong to the committed feed.",
                nameof(through)
            );

        string organization = database.RequiredOrganizationKey;
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM sales.customer_profile_changes WHERE organization_key = {organization} AND position <= {through.AfterPosition}
            """,
            cancellationToken
        );
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE sales.customer_profile_feed_state SET floor = GREATEST(floor, {through.AfterPosition}) WHERE organization_key = {organization}
            """,
            cancellationToken
        );
        await transaction.CommitAsync(cancellationToken);
    }
}
