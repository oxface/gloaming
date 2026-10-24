namespace Gloaming.Samples.Wholesale.Inventory;

internal sealed class StockRow
{
    public required Guid Id { get; init; }
    public required string OrganizationKey { get; init; }
    public required string Sku { get; init; }
    public required int AvailableQuantity { get; set; }
}
