using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales.ProfileFeed;

internal sealed class CustomerProfileFeed(SalesDbContext database) : ICustomerProfileFeed
{
    public async Task<CustomerProfileFeedBounds> DescribeAsync(CancellationToken cancellationToken)
    {
        FeedStorage.RequireIndependent(database);
        return FeedStorage.Bounds(await ReadStateAsync(cancellationToken));
    }

    public async Task<CustomerProfileFeedRead> ReadAsync(
        CustomerProfileFeedCursor after,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        FeedStorage.ValidateCursor(after);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);

        FeedStorage.RequireIndependent(database);
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken
        );
        await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var state = await ReadStateAsync(cancellationToken);
        var bounds = FeedStorage.Bounds(state);
        if (after.Generation != state.Generation || after.AfterPosition > state.Head)
            return new CustomerProfileFeedRead.InvalidCursor();

        if (after.AfterPosition < state.Floor)
            return new CustomerProfileFeedRead.CursorExpired(bounds);

        var rows = await database
            .Set<ProfileChangeRow>()
            .AsNoTracking()
            .Where(row => row.Position > after.AfterPosition && row.Position <= state.Head)
            .OrderBy(row => row.Position)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);
        bool more = rows.Length > pageSize;
        var changes = rows.Take(pageSize)
            .Select(row =>
            {
                var profile =
                    row.Payload.Deserialize<CustomerProfileChangedV1>()
                    ?? throw new InvalidDataException("Missing Sales feed V1 payload.");
                FeedStorage.ValidateProfile(profile);
                if (row.ProfileVersion != profile.Version || row.ChangeId == Guid.Empty)
                    throw new InvalidDataException(
                        "Sales feed envelope disagrees with its payload."
                    );

                return new CustomerProfileFeedChange(row.Position, row.ChangeId, profile);
            })
            .ToArray();
        long next = more ? changes[^1].Position : state.Head;
        await transaction.CommitAsync(cancellationToken);
        return new CustomerProfileFeedRead.Page(
            bounds,
            Array.AsReadOnly(changes),
            new(state.Generation, next),
            more
        );
    }

    private async Task<FeedStateRow> ReadStateAsync(CancellationToken token) =>
        await database.Set<FeedStateRow>().AsNoTracking().SingleOrDefaultAsync(token)
        ?? throw new InvalidOperationException("The Sales profile feed has not been activated.");
}
