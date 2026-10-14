using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;

internal sealed class DraftOrderRow
{
    public Guid Id { get; set; }
    public required string OrganizationKey { get; set; }
    public Guid CustomerId { get; set; }
    public required string Code { get; set; }
    public long Version { get; set; }
    public required string Currency { get; set; }
    public decimal TotalLimit { get; set; }
    public List<DraftOrderLineRow> Lines { get; } = [];

    internal DraftOrderAggregate ToAggregate(IEnumerable<DraftOrderLineRow>? lines = null) =>
        DraftOrderAggregate.FromState(
            Id,
            CustomerId,
            Code,
            Version,
            Currency,
            TotalLimit,
            (lines ?? Lines).Select(line => new DraftOrderLine(
                line.Id,
                line.Sku,
                line.Quantity,
                line.UnitPrice
            ))
        );

    internal static DraftOrderRow Create(string owner, DraftOrderAggregate aggregate)
    {
        var row = new DraftOrderRow
        {
            Id = aggregate.Id,
            OrganizationKey = owner,
            CustomerId = aggregate.CustomerId,
            Code = aggregate.Code,
            Version = aggregate.Version,
            Currency = aggregate.Limit.Currency,
            TotalLimit = aggregate.Limit.Amount,
        };
        row.Lines.AddRange(aggregate.Lines.Select(line => DraftOrderLineRow.Create(row, line)));
        return row;
    }
}

internal sealed class DraftOrderLineRow
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public required string OrganizationKey { get; set; }
    public required string Sku { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    internal static DraftOrderLineRow Create(DraftOrderRow parent, OrderLine line) =>
        new()
        {
            Id = line.Id,
            OrderId = parent.Id,
            OrganizationKey = parent.OrganizationKey,
            Sku = line.Sku,
            Quantity = line.Quantity,
            UnitPrice = line.Price.Amount,
        };
}
