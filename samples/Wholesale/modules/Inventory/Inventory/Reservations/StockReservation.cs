using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Inventory.Reservations;

// Business receipt survives inbox retention and records the original decision after release.
internal sealed class StockReservation
{
    public string OrganizationKey { get; private set; } = null!;
    public Guid Id { get; private set; }
    public Guid StockPositionId { get; private set; }
    public long ExpectedVersion { get; private set; }
    public decimal Quantity { get; private set; }
    public StockReservationDecision Decision { get; private set; }
    public bool Released { get; private set; }
    public long Version { get; private set; }

    internal static StockReservation Create(
        string organization,
        ReserveStockV1 command,
        StockReservationDecision decision
    ) =>
        new()
        {
            OrganizationKey = organization,
            Id = command.ReservationId,
            StockPositionId = command.StockPositionId,
            ExpectedVersion = command.ExpectedVersion,
            Quantity = command.Quantity,
            Decision = decision,
            Version = 1,
        };

    internal void ValidateRetry(ReserveStockV1 command)
    {
        if (
            StockPositionId != command.StockPositionId
            || ExpectedVersion != command.ExpectedVersion
            || Quantity != command.Quantity
        )
            throw new InvalidDataException(
                "Reservation identity was reused with different inputs."
            );
    }

    internal void Release()
    {
        Released = true;
        Version = checked(Version + 1);
    }

    internal StockReservationDecidedV1 Outcome() =>
        new(Id, StockPositionId, ExpectedVersion, Quantity, Decision);
}
