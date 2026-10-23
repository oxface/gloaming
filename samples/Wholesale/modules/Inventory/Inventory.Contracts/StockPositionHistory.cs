namespace Gloaming.Samples.Wholesale.Inventory.Contracts;

public sealed record StockPositionHistory(
    Guid Id,
    long Version,
    DateTimeOffset RecordedAt,
    Guid StockItemId,
    Guid StockingLocationId,
    string BaseUnitCode,
    decimal OnHand,
    string? LatestDeliveryReference,
    decimal Reserved = 0
)
{
    public decimal Available => OnHand - Reserved;
}
