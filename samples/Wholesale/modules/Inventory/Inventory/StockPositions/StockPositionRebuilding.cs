using Gloaming.EventSourcing.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

// Maintenance authorization belongs to the host; admitted Organization and native transaction are required here.
internal sealed class StockPositionRebuilding(
    InventoryDbContext database,
    IAggregateRebuilder<StockPositionAggregate> rebuilder
) : IStockPositionRebuilding
{
    public async Task<StockPositionRebuildResult> RebuildAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        _ = database.RequiredOrganizationKey;
        var result = await rebuilder.RebuildAsync(id, cancellationToken);
        return result is null
            ? new StockPositionRebuildResult.NotFound()
            : new StockPositionRebuildResult.Changed(result.Version, result.RecordedAt);
    }
}
