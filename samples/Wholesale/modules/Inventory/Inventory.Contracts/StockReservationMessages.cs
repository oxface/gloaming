using System.Text.Json.Serialization;

namespace ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

/// <summary>Reserves available stock under a stable business identity, independent of delivery identity.</summary>
public sealed record ReserveStockV1(
    [property: JsonRequired] Guid ReservationId,
    [property: JsonRequired] Guid StockPositionId,
    [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] decimal Quantity
);

/// <summary>Releases a reservation using current stock state, including after unrelated stock changes.</summary>
public sealed record ReleaseStockReservationV1([property: JsonRequired] Guid ReservationId);

/// <summary>The original reserve decision; retries retain this outcome even after release.</summary>
public sealed record StockReservationDecidedV1(
    [property: JsonRequired] Guid ReservationId,
    [property: JsonRequired] Guid StockPositionId,
    [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] decimal Quantity,
    [property: JsonRequired] StockReservationDecision Decision
);

/// <summary>A release outcome acknowledges compensation; a refusal requires consumer intervention.</summary>
public sealed record StockReservationReleasedV1(
    [property: JsonRequired] Guid ReservationId,
    [property: JsonRequired] StockReservationReleaseDecision Decision
);

public enum StockReservationDecision
{
    Reserved = 1,
    NotFound = 2,
    Conflict = 3,
    InsufficientStock = 4,
}

public enum StockReservationReleaseDecision
{
    Released = 1,
    ReservationNotFound = 2,
    StockPositionNotFound = 3,
}
