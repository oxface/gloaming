namespace Gloaming.Samples.Wholesale.Inventory.Contracts;

// Staging changes the caller's native context only. Staged is not committed success.
public interface IStockPositionCommands
{
    Task<StockPositionChangeResult> OpenAsync(
        OpenStockPosition request,
        CancellationToken cancellationToken
    );
    Task<StockPositionChangeResult> ReceiveAsync(
        ReceiveStock request,
        CancellationToken cancellationToken
    );
    Task<StockPositionChangeResult> IssueAsync(
        IssueStock request,
        CancellationToken cancellationToken
    );
}

public sealed record OpenStockPosition(
    Guid Id,
    Guid StockItemId,
    Guid StockingLocationId,
    string BaseUnitCode,
    long ExpectedVersion = 0
);

public sealed record StockReceipt(decimal Quantity, string? DeliveryReference = null);

public sealed record ReceiveStock(
    Guid Id,
    long ExpectedVersion,
    IReadOnlyList<StockReceipt> Receipts
);

public sealed record StockIssue(decimal Quantity);

public sealed record IssueStock(Guid Id, long ExpectedVersion, IReadOnlyList<StockIssue> Issues);

public abstract record StockPositionChangeResult
{
    public sealed record Changed(StockPositionHistory Proposed) : StockPositionChangeResult;

    public sealed record NotFound : StockPositionChangeResult;

    public sealed record Conflict : StockPositionChangeResult;

    public sealed record InsufficientStock(decimal Available, decimal Requested)
        : StockPositionChangeResult;
}
