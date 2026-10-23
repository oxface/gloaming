namespace Gloaming.Samples.Wholesale.Inventory.Contracts;

public interface IStockPositionQueries
{
    Task<StockPositionHistory?> ReadCurrentAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StockPositionHistory>> ReadAvailableAsync(
        decimal requiredQuantity,
        CancellationToken cancellationToken
    );
}
