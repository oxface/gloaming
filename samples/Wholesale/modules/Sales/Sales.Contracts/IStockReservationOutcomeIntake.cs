using Gloaming.Samples.Wholesale.Inventory.Contracts;

namespace Gloaming.Samples.Wholesale.Sales.Contracts;

/// <summary>Durable Inventory-to-Sales intake. Completion means committed receipt; the workflow advances separately.</summary>
public interface IStockReservationOutcomeIntake
{
    Task ReceiveAsync(
        ReservationDelivery delivery,
        StockReservationDecidedV1 outcome,
        CancellationToken cancellationToken
    );
    Task ReceiveAsync(
        ReservationDelivery delivery,
        StockReservationReleasedV1 outcome,
        CancellationToken cancellationToken
    );
}
