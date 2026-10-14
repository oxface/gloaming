using System.Text.Json;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo;

public sealed class FeedJournal(FeedJournalDbContext database)
{
    public async Task InitializeAsync(
        Guid id,
        string organization,
        CustomerProfileFeedCursor start,
        CancellationToken token
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ValidateCursor(start);

        RequireIndependent();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var existing = await database.Subscriptions.FindAsync([id], token);
        if (existing is not null)
        {
            if (existing.Organization != organization || existing.Generation != start.Generation)
                throw new InvalidDataException(
                    "The journal subscription is already bound to another source."
                );

            return;
        }

        database.Subscriptions.Add(
            new()
            {
                Id = id,
                Organization = organization,
                Generation = start.Generation,
                Position = start.AfterPosition,
            }
        );
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task<CustomerProfileFeedCursor> ReadAsync(
        Guid id,
        string organization,
        CancellationToken token
    )
    {
        var row = await database
            .Subscriptions.AsNoTracking()
            .SingleAsync(row => row.Id == id && row.Organization == organization, token);
        return new(row.Generation, row.Position);
    }

    public async Task<long> CountAsync(Guid id, CancellationToken token) =>
        await database.Receipts.LongCountAsync(row => row.SubscriptionId == id, token);

    public async Task RecordAndCheckpointAsync(
        Guid id,
        string organization,
        CustomerProfileFeedCursor expected,
        CustomerProfileFeedRead.Page page,
        CancellationToken token
    )
    {
        ValidateCursor(expected);
        ValidatePage(expected, page);

        RequireIndependent();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var subscription = await database.Subscriptions.SingleAsync(
            row => row.Id == id && row.Organization == organization,
            token
        );
        if (subscription.Generation != expected.Generation)
            throw new InvalidDataException("Journal source generation changed.");

        bool duplicate = subscription.Position >= page.Next.AfterPosition;
        if (!duplicate && subscription.Position != expected.AfterPosition)
            throw new InvalidOperationException(
                "Stale page: reload the committed checkpoint and read again."
            );

        foreach (var change in page.Changes)
        {
            var receipt = await database.Receipts.FindAsync(
                [id, expected.Generation, change.ChangeId],
                token
            );
            if (receipt is not null)
            {
                if (
                    receipt.Position != change.Position
                    || receipt.Payload.Deserialize<CustomerProfileChangedV1>() != change.Profile
                )
                    throw new InvalidDataException(
                        "A retained change identity has conflicting content."
                    );
            }
            else
            {
                if (duplicate)
                    throw new InvalidDataException(
                        "Checkpoint advanced without this change's receipt."
                    );

                database.Receipts.Add(
                    new()
                    {
                        SubscriptionId = id,
                        Generation = expected.Generation,
                        ChangeId = change.ChangeId,
                        Position = change.Position,
                        Payload = JsonSerializer.SerializeToElement(change.Profile),
                    }
                );
            }
        }

        if (!duplicate)
        {
            subscription.Position = page.Next.AfterPosition;
            subscription.Version = checked(subscription.Version + 1);
            await database.SaveChangesAsync(token);
        }

        await transaction.CommitAsync(token);
    }

    private void RequireIndependent()
    {
        if (
            database.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null
            || database.Database.GetEnlistedTransaction() is not null
            || database.ChangeTracker.HasChanges()
        )
            throw new InvalidOperationException(
                "Journal operations require a fresh context without a transaction or pending changes."
            );
    }

    private static void ValidateCursor(CustomerProfileFeedCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentOutOfRangeException.ThrowIfEqual(cursor.Generation, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(cursor.AfterPosition);
    }

    private static void ValidatePage(
        CustomerProfileFeedCursor expected,
        CustomerProfileFeedRead.Page page
    )
    {
        ArgumentNullException.ThrowIfNull(page);
        ValidateCursor(page.Next);
        ValidateCursor(page.Bounds.EarliestAvailable);
        ValidateCursor(page.Bounds.Head);

        if (
            page.Next.Generation != expected.Generation
            || page.Bounds.Head.Generation != expected.Generation
            || page.Bounds.EarliestAvailable.Generation != expected.Generation
            || page.Bounds.EarliestAvailable.AfterPosition > expected.AfterPosition
            || page.Next.AfterPosition < expected.AfterPosition
            || page.Next.AfterPosition > page.Bounds.Head.AfterPosition
            || page.Changes.Count > 100
            || (!page.HasMore && page.Next != page.Bounds.Head)
            || (
                page.HasMore
                && (page.Changes.Count == 0 || page.Next.AfterPosition != page.Changes[^1].Position)
            )
        )
            throw new InvalidDataException("Invalid source page bounds.");

        long previous = expected.AfterPosition;
        var identities = new HashSet<Guid>();
        foreach (var change in page.Changes)
        {
            if (
                change.Position <= previous
                || change.Position > page.Next.AfterPosition
                || change.ChangeId == Guid.Empty
                || !identities.Add(change.ChangeId)
                || change.Profile is null
                || change.Profile.Version < 1
                || change.Profile.CustomerId == Guid.Empty
                || change.Profile.AddressId == Guid.Empty
            )
                throw new InvalidDataException("Invalid source page change.");

            previous = change.Position;
        }
    }
}
