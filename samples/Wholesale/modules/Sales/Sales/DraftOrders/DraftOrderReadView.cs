namespace Gloaming.Samples.Wholesale.Sales.DraftOrders;

// Query-time shape over Sales-owned rows, never a mapped or persisted projection.
internal sealed class DraftOrderReadView
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public long Version { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = null!;
    public string Currency { get; init; } = null!;
    public int LineCount { get; init; }
    public decimal Total { get; init; }
}
