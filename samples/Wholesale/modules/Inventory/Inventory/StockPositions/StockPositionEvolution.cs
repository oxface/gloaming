using Gloaming.Samples.Wholesale.Inventory.Contracts;

namespace Gloaming.Samples.Wholesale.Inventory.StockPositions;

internal static class StockPositionEvolution
{
    internal static StockPositionHistory Rehydrate(
        Guid id,
        long version,
        DateTimeOffset recordedAt,
        IEnumerable<IStockPositionEvent> events
    ) => Evolve(null, events).ToHistory(id, version, recordedAt);

    internal static StockPositionState Evolve(
        StockPositionState? state,
        IEnumerable<IStockPositionEvent> events
    )
    {
        foreach (IStockPositionEvent @event in events)
            state = @event switch
            {
                StockPositionOpened opened
                    when state is null
                        && opened.StockItemId != Guid.Empty
                        && opened.StockingLocationId != Guid.Empty
                        && !string.IsNullOrWhiteSpace(opened.BaseUnitCode) =>
                    new StockPositionState(
                        opened.StockItemId,
                        opened.StockingLocationId,
                        opened.BaseUnitCode,
                        0,
                        null
                    ),
                StockPositionReceived received when state is not null && received.Quantity > 0 =>
                    state with
                    {
                        OnHand = state.OnHand + received.Quantity,
                        LatestDeliveryReference = received.DeliveryReference,
                    },
                StockPositionIssued issued when state is not null && issued.Quantity > 0 =>
                    state with
                    {
                        OnHand = state.OnHand - issued.Quantity,
                    },
                StockPositionReserved reserved
                    when state is not null
                        && reserved.ReservationId != Guid.Empty
                        && reserved.Quantity > 0
                        && reserved.Quantity <= state.Available => state with
                {
                    Reserved = state.Reserved + reserved.Quantity,
                },
                StockPositionReservationReleased released
                    when state is not null
                        && released.ReservationId != Guid.Empty
                        && released.Quantity > 0
                        && released.Quantity <= state.Reserved => state with
                {
                    Reserved = state.Reserved - released.Quantity,
                },
                _ => throw new InvalidOperationException(
                    "Invalid stock-position event sequence or business values."
                ),
            };
        return state
            ?? throw new InvalidOperationException("The stock position has not been opened.");
    }
}
