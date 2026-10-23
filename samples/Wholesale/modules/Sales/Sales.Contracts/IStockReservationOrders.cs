namespace ModulithFoundry.Samples.Wholesale.Sales.Contracts;

/// <summary>Durably reserves two distinct stock lines, compensating the first if the second is refused.</summary>
public interface IStockReservationOrders
{
    Task<StockReservationOrder> StartAsync(
        StartStockReservationOrder request,
        CancellationToken cancellationToken
    );
    Task<StockReservationOrder?> ReadAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Explicit operator retry after a refused compensation. Reuse operationId for equal retries to recover the original result; a new operator decision requires a new identity.</summary>
    Task<StockReservationOrder> RetryCompensationAsync(
        Guid orderId,
        Guid operationId,
        CancellationToken cancellationToken
    );
}

public sealed record StockReservationLine(
    Guid StockPositionId,
    long ExpectedVersion,
    decimal Quantity
);

public sealed record StartStockReservationOrder(
    Guid OrderId,
    StockReservationLine First,
    StockReservationLine Second
);

public sealed record StockReservationOrder(
    Guid OrderId,
    StockReservationOrderStatus Status,
    long Version,
    Guid FirstReservationId,
    Guid SecondReservationId,
    string? Refusal,
    string? CompensationRefusal
);

public enum StockReservationOrderStatus
{
    AwaitingFirst = 1,
    AwaitingSecond = 2,
    Reserved = 3,
    Releasing = 4,
    Compensated = 5,
    Declined = 6,
    NeedsAttention = 7,
}
