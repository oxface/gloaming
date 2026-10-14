using Gloaming.EventSourcing.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

// Maintenance authorization belongs to the host; admitted Organization and native transaction are required here.
internal sealed class PurchaseOrderRebuilding(
    PurchasingDbContext database,
    IAggregateRebuilder<PurchaseOrderAggregate> rebuilder
) : IPurchaseOrderRebuilding
{
    public async Task<PurchaseOrderRebuildResult> RebuildAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        _ = database.RequiredOrganizationKey;
        var result = await rebuilder.RebuildAsync(id, cancellationToken);
        return result is null
            ? new PurchaseOrderRebuildResult.NotFound()
            : new PurchaseOrderRebuildResult.Changed(result.Version, result.RecordedAt);
    }
}
