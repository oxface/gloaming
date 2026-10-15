namespace ModulithFoundry.Samples.Wholesale.Sales.Contracts;

public interface IDraftOrderQueries
{
    Task<DraftOrder?> ReadAsync(Guid id, CancellationToken cancellationToken);

    Task<DraftOrderSummary?> ReadSummaryAsync(Guid id, CancellationToken cancellationToken);

    // Count and page share predicates. Use a caller-owned repeatable-read transaction
    // when both statements must observe one snapshot. Results sort by total, then ID.
    Task<DraftOrderPage> SearchAsync(
        SearchDraftOrders request,
        CancellationToken cancellationToken
    );
}

public sealed record SearchDraftOrders(
    string? CustomerNameContains = null,
    string? Currency = null,
    decimal? MinimumTotal = null,
    int Offset = 0,
    int Limit = 20
);

public sealed record DraftOrderPage(IReadOnlyList<DraftOrderSummary> Items, int TotalCount);

public sealed record DraftOrderLine(Guid Id, string Sku, decimal Quantity, decimal UnitPrice);

public sealed record DraftOrder(
    Guid Id,
    Guid CustomerId,
    string Code,
    long Version,
    string Currency,
    decimal TotalLimit,
    decimal Total,
    IReadOnlyList<DraftOrderLine> Lines,
    bool IsSubmitted = false
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
