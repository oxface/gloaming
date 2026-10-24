using Gloaming.Samples.Wholesale.Inventory.Contracts;

namespace Gloaming.Samples.Wholesale.Inventory.StockPositions;

internal sealed record StockPositionState(
    Guid StockItemId,
    Guid StockingLocationId,
    string BaseUnitCode,
    decimal OnHand,
    string? LatestDeliveryReference,
    decimal Reserved = 0
)
{
    internal decimal Available => OnHand - Reserved;

    internal StockPositionHistory ToHistory(Guid id, long version, DateTimeOffset recordedAt) =>
        new(
            id,
            version,
            recordedAt,
            StockItemId,
            StockingLocationId,
            BaseUnitCode,
            OnHand,
            LatestDeliveryReference,
            Reserved
        );
}
