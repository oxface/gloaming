namespace ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

/// <summary>Durable Sales-to-Inventory intake. Completion means committed receipt, not a reservation decision.</summary>
public interface IStockReservationIntake
{
    Task ReceiveAsync(
        ReservationDelivery delivery,
        ReserveStockV1 command,
        CancellationToken cancellationToken
    );
    Task ReceiveAsync(
        ReservationDelivery delivery,
        ReleaseStockReservationV1 command,
        CancellationToken cancellationToken
    );
}

/// <summary>Delivery identity and admitted process metadata; retries retain these values and the original payload.</summary>
public sealed record ReservationDelivery(
    Guid MessageId,
    string OrganizationKey,
    Guid OrderId,
    Guid? CauseMessageId,
    string? TraceParent = null,
    string? TraceState = null
);
