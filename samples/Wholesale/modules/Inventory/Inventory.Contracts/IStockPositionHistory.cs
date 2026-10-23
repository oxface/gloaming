namespace Gloaming.Samples.Wholesale.Inventory.Contracts;

public interface IStockPositionHistory
{
    Task<StockPositionHistory?> ReadCurrentAsync(Guid id, CancellationToken cancellationToken);
    Task<StockPositionHistory?> ReadAtVersionAsync(
        Guid id,
        long version,
        CancellationToken cancellationToken
    );
    Task<StockPositionHistory?> ReadAsOfAsync(
        Guid id,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    );
}
