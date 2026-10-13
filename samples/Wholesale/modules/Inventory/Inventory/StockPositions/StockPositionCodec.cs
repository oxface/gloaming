using System.Text.Json;
using Rootbolt.Events.Serialization;

namespace ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

internal static class StockPositionCodec
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
                    2
                ),
                EventRegistration<IStockPositionEvent>.For<StockPositionIssued>(
                    "inventory.stock-position.issued",
                    1
                ),
                EventRegistration<IStockPositionEvent>.For<StockPositionReserved>(
                    "inventory.stock-position.reserved",
                    1
                ),
                EventRegistration<IStockPositionEvent>.For<StockPositionReservationReleased>(
                    "inventory.stock-position.reservation-released",
                    1
                ),
            ],
            [new ReceiptV1ToV2()]
        );
}
