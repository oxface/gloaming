namespace ModulithFoundry.Samples.Wholesale.Sales.Contracts;

public interface IDraftOrderQueries
{
    Task<DraftOrder?> ReadAsync(Guid id, CancellationToken cancellationToken);

    Task<DraftOrderSummary?> ReadSummaryAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record DraftOrderLine(Guid Id, string Sku, decimal Quantity, decimal UnitPrice);

public sealed record DraftOrder(
    Guid Id,
    Guid CustomerId,
    string Code,
    long Version,
    string Currency,
    decimal TotalLimit,
    decimal Total,
    IReadOnlyList<DraftOrderLine> Lines
);

public sealed record DraftOrderSummary(
    Guid Id,
    string Code,
    long Version,
    Guid CustomerId,
    string CustomerName,
    string Currency,
    int LineCount,
    decimal Total
);
