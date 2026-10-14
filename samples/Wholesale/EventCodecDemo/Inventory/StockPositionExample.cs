using System.Text.Json;
using Gloaming.Events.Serialization;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo.Inventory;

internal static class StockPositionExample
{
    internal static JsonEventCodec<IStockPositionEvent> CreateCodec() =>
        new(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                RespectNullableAnnotations = true,
            },
            [
                EventRegistration<IStockPositionEvent>.For<StockPositionOpened>(
                    "inventory.stock-position.opened",
                    1
                ),
                EventRegistration<IStockPositionEvent>.For<StockPositionReceived>(
                    "inventory.stock-position.received",
                    1
                ),
            ]
        );

    internal static StockPositionSummary Read(IEnumerable<IStockPositionEvent> events)
    {
        StockPositionSummary? state = null;
        foreach (IStockPositionEvent @event in events)
            state = @event switch
            {
                StockPositionOpened opened when state is null => new StockPositionSummary(
                    opened.StockItemId,
                    opened.StockingLocationId,
                    opened.BaseUnitCode,
                    0,
                    null
                ),
                StockPositionReceived received when state is not null => state with
                {
                    OnHand = state.OnHand + received.Quantity,
                    LatestDeliveryReference = received.DeliveryReference,
                },
                _ => throw new InvalidOperationException(
                    "Unsupported stock-position example sequence."
                ),
            };
        return state
            ?? throw new InvalidOperationException("The example has no opened stock position.");
    }
}

internal sealed record StockPositionSummary(
    Guid StockItemId,
    Guid StockingLocationId,
    string BaseUnitCode,
    decimal OnHand,
    string? LatestDeliveryReference
);
