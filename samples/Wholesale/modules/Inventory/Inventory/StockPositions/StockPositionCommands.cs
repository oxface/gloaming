using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Inventory.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Inventory.StockPositions;

internal sealed class StockPositionCommands(
    IEventStore<StockPositionAggregate> store,
    IOutbox<InventoryDbContext> outbox,
    StockIssueMessages messages
) : IStockPositionCommands
{
    public async Task<StockPositionChangeResult> OpenAsync(
        OpenStockPosition request,
        CancellationToken cancellationToken
    )
    {
        StockPositionDecisions.ValidateOpen(request);
        try
        {
            var aggregate = await store.GetForWritingAsync(
                request.Id,
                request.ExpectedVersion,
                cancellationToken
            );
            aggregate ??= StockPositionAggregate.Create(request);
            var recordedAt = (await store.AppendAsync(aggregate, cancellationToken)).RecordedAt;
            return new StockPositionChangeResult.Changed(
                aggregate.State!.ToHistory(aggregate.Id, aggregate.Version, recordedAt)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return new StockPositionChangeResult.Conflict();
        }
    }

    public async Task<StockPositionChangeResult> ReceiveAsync(
        ReceiveStock request,
        CancellationToken cancellationToken
    )
    {
        var items = StockPositionDecisions.ReceiptItems(request);
        try
        {
            var aggregate = await store.GetForWritingAsync(
                request.Id,
                request.ExpectedVersion,
                cancellationToken
            );
            if (aggregate is null)
                return new StockPositionChangeResult.NotFound();
            aggregate.Receive(items);
            var recordedAt = (await store.AppendAsync(aggregate, cancellationToken)).RecordedAt;
            return new StockPositionChangeResult.Changed(
                aggregate.State!.ToHistory(aggregate.Id, aggregate.Version, recordedAt)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return new StockPositionChangeResult.Conflict();
        }
    }

    public async Task<StockPositionChangeResult> IssueAsync(
        IssueStock request,
        CancellationToken cancellationToken
    )
    {
        var quantities = StockPositionDecisions.IssueQuantities(request);
        try
        {
            var aggregate = await store.GetForWritingAsync(
                request.Id,
                request.ExpectedVersion,
                cancellationToken
            );
            if (aggregate is null)
                return new StockPositionChangeResult.NotFound();
            if (!aggregate.TryIssue(quantities, out decimal requested))
                return new StockPositionChangeResult.InsufficientStock(
                    aggregate.State!.Available,
                    requested
                );
            var recordedAt = (await store.AppendAsync(aggregate, cancellationToken)).RecordedAt;
            // Accepted facts and this explicitly selected integration contract share the caller's transaction.
            outbox.Enqueue(messages.StockIssueRecorded(aggregate, requested, recordedAt));
            return new StockPositionChangeResult.Changed(
                aggregate.State!.ToHistory(aggregate.Id, aggregate.Version, recordedAt)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return new StockPositionChangeResult.Conflict();
        }
    }
}
