using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Sales.Contracts;

namespace Gloaming.Samples.Wholesale.Worker.Local.Delivery;

// Relay code depends only on public Contracts. It never executes a business decision or opens a receiver context.
public sealed class ReservationCommandPublisher(IStockReservationIntake intake) : IMessagePublisher
{
    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        var delivery = ReservationHandoff.Metadata(message);
        return (message.RouteKey, message.MessageName) switch
        {
            ("inventory.reserve-stock", "inventory.reserve-stock") => intake.ReceiveAsync(
                delivery,
                ReservationHandoff.Read<ReserveStockV1>(message),
                cancellationToken
            ),
            ("inventory.release-reservation", "inventory.release-reservation") =>
                intake.ReceiveAsync(
                    delivery,
                    ReservationHandoff.Read<ReleaseStockReservationV1>(message),
                    cancellationToken
                ),
            _ => throw new InvalidDataException(
                "The local reservation command route is unsupported."
            ),
        };
    }
}

public sealed class ReservationOutcomePublisher(IStockReservationOutcomeIntake intake)
    : IMessagePublisher
{
    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        var delivery = ReservationHandoff.Metadata(message);
        if (message.RouteKey != "inventory.reservation-outcomes")
            throw new InvalidDataException("The local reservation outcome route is unsupported.");

        return message.MessageName switch
        {
            "inventory.reservation-decided" => intake.ReceiveAsync(
                delivery,
                ReservationHandoff.Read<StockReservationDecidedV1>(message),
                cancellationToken
            ),
            "inventory.reservation-released" => intake.ReceiveAsync(
                delivery,
                ReservationHandoff.Read<StockReservationReleasedV1>(message),
                cancellationToken
            ),
            _ => throw new InvalidDataException(
                "The local reservation outcome alias is unsupported."
            ),
        };
    }
}

internal static class ReservationHandoff
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static ReservationDelivery Metadata(OutgoingMessage message)
    {
        if (
            message.SchemaVersion != 1
            || string.IsNullOrWhiteSpace(message.TenantKey)
            || !Guid.TryParseExact(message.CorrelationId, "D", out Guid order)
            || order == Guid.Empty
        )
            throw new InvalidDataException("The local reservation delivery metadata is invalid.");

        Guid? cause = null;
        if (message.CausationId is not null)
        {
            if (!Guid.TryParseExact(message.CausationId, "D", out Guid id) || id == Guid.Empty)
                throw new InvalidDataException("The reservation cause is invalid.");

            cause = id;
        }

        return new(
            message.MessageId,
            message.TenantKey,
            order,
            cause,
            message.TraceParent,
            message.TraceState
        );
    }

    internal static T Read<T>(OutgoingMessage message) =>
        message.Payload.Deserialize<T>(Json)
        ?? throw new InvalidDataException("The reservation payload is missing.");
}
