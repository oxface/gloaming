using System.Text.Json.Serialization;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo.Inventory;

internal interface IStockPositionEvent;

internal sealed record StockPositionOpened(
    [property: JsonRequired] Guid StockItemId,
    [property: JsonRequired] Guid StockingLocationId,
    [property: JsonRequired] string BaseUnitCode
) : IStockPositionEvent;

// The durable alias stays stock-position.received; CLR naming is consumer-owned.
internal sealed record StockPositionReceived(
    [property: JsonRequired] decimal Quantity,
    string? DeliveryReference = null
) : IStockPositionEvent;
