namespace Gloaming.Samples.Wholesale.Inventory.Contracts;

public interface IStockPositionRebuilding
{
    Task<StockPositionRebuildResult> RebuildAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );
}

public abstract record StockPositionRebuildResult
{
    private StockPositionRebuildResult() { }

    public sealed record NotFound : StockPositionRebuildResult;

    public sealed record Changed(long Version, DateTimeOffset RecordedAt)
        : StockPositionRebuildResult;
}
