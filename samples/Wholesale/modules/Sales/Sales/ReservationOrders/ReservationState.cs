using Gloaming.Messaging.Workflows;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales.ReservationOrders;

// Business identities survive technical operation retries; this is ordinary serialized consumer state.
internal sealed record ReservationState(
    ReserveStockV1 First,
    ReserveStockV1 Second,
    StockReservationOrderStatus Status,
    string? Refusal = null,
    string? CompensationRefusal = null
)
{
    internal static ReservationState Create(StartStockReservationOrder input) =>
        new(
            new(
                Guid.NewGuid(),
                input.First.StockPositionId,
                input.First.ExpectedVersion,
                input.First.Quantity
            ),
            new(
                Guid.NewGuid(),
                input.Second.StockPositionId,
                input.Second.ExpectedVersion,
                input.Second.Quantity
            ),
            StockReservationOrderStatus.AwaitingFirst
        );

    internal static void ValidateReservationReply(
        ReserveStockV1 expected,
        StockReservationDecidedV1 reply
    )
    {
        if (
            reply.ReservationId != expected.ReservationId
            || reply.StockPositionId != expected.StockPositionId
            || reply.ExpectedVersion != expected.ExpectedVersion
            || reply.Quantity != expected.Quantity
        )
            throw new InvalidDataException(
                "The reserve outcome does not match the requested line."
            );
    }

    internal static void ValidateReleaseReply(
        ReserveStockV1 expected,
        StockReservationReleasedV1 reply
    )
    {
        if (reply.ReservationId != expected.ReservationId)
            throw new InvalidDataException(
                "The release outcome does not match the requested reservation."
            );
    }

    internal static Sales.Contracts.StockReservationOrder Read(
        WorkflowSnapshot<ReservationState> snapshot
    ) =>
        new(
            snapshot.Key.Id,
            snapshot.State.Status,
            snapshot.Version,
            snapshot.State.First.ReservationId,
            snapshot.State.Second.ReservationId,
            snapshot.State.Refusal,
            snapshot.State.CompensationRefusal
        );
}
