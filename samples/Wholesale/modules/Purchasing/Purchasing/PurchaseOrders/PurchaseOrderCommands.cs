using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal sealed class PurchaseOrderCommands(IEventStore<PurchaseOrderAggregate> store)
    : IPurchaseOrderCommands
{
    public async Task<PurchaseOrderChangeResult> DraftAsync(
        DraftPurchaseOrder request,
        CancellationToken cancellationToken
    )
    {
        PurchaseOrderDecisions.ValidateDraft(request);
        try
        {
            var aggregate = await store.GetForWritingAsync(
                request.Id,
                request.ExpectedVersion,
                cancellationToken
            );
            aggregate ??= PurchaseOrderAggregate.Create(request);
            var recordedAt = (await store.AppendAsync(aggregate, cancellationToken)).RecordedAt;
            return new PurchaseOrderChangeResult.Changed(
                aggregate.State!.ToHistory(aggregate.Id, aggregate.Version, recordedAt)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return new PurchaseOrderChangeResult.Conflict();
        }
    }

    public async Task<PurchaseOrderChangeResult> ChangeLinesAsync(
        ChangePurchaseOrderLines request,
        CancellationToken cancellationToken
    )
    {
        var items = PurchaseOrderDecisions.LineItems(request);
        try
        {
            var aggregate = await store.GetForWritingAsync(
                request.Id,
                request.ExpectedVersion,
                cancellationToken
            );
            if (aggregate is null)
                return new PurchaseOrderChangeResult.NotFound();
            aggregate.SetLines(items);
            var recordedAt = (await store.AppendAsync(aggregate, cancellationToken)).RecordedAt;
            return new PurchaseOrderChangeResult.Changed(
                aggregate.State!.ToHistory(aggregate.Id, aggregate.Version, recordedAt)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return new PurchaseOrderChangeResult.Conflict();
        }
    }
}
