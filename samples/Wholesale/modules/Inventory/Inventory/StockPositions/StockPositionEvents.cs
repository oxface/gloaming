using System.Text.Json.Serialization;

namespace ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

internal interface IStockPositionEvent;

internal sealed record StockPositionOpened(
    [property: JsonRequired] Guid StockItemId,
    [property: JsonRequired] Guid StockingLocationId,
    [property: JsonRequired] string BaseUnitCode
) : IStockPositionEvent;

// The durable alias stays stock-position.received; CLR naming is consumer-owned.
internal sealed record StockPositionReceived(
    [property: JsonRequired, JsonPropertyName("receivedQuantity")] decimal Quantity,
    string? DeliveryReference = null
) : IStockPositionEvent;

internal sealed record StockPositionIssued([property: JsonRequired] decimal Quantity)
    : IStockPositionEvent;

internal sealed record StockPositionReserved(
    [property: JsonRequired] Guid ReservationId,
    [property: JsonRequired] decimal Quantity
) : IStockPositionEvent;

internal sealed record StockPositionReservationReleased(
    [property: JsonRequired] Guid ReservationId,
    [property: JsonRequired] decimal Quantity
) : IStockPositionEvent;
