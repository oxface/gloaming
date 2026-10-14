using System.Text.Json;
using Gloaming.Events.Serialization;
using Gloaming.Messaging;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Inventory;

/// <summary>Consumer-owned reservation wire admission; transport binds the Sales producer.</summary>
public static class StockReservationAdmission
{
    public const string Subscription = "inventory.reservations";
    private static readonly JsonEventCodec<object> Codec = new(
        new JsonSerializerOptions(JsonSerializerDefaults.Web),
        [
            EventRegistration<object>.For<ReserveStockV1>("inventory.reserve-stock", 1),
            EventRegistration<object>.For<ReleaseStockReservationV1>(
                "inventory.release-reservation",
                1
            ),
        ]
    );

    public static void Validate(IncomingMessage message) => _ = Decode(message);

    internal static object Decode(IncomingMessage message)
    {
        if (
            message.ProducerKey != "sales"
            || message.TenantKey is not ("wholesale-alpha" or "wholesale-beta")
            || !Guid.TryParseExact(message.CorrelationId, "D", out Guid orderId)
            || orderId == Guid.Empty
        )
            throw new InvalidDataException(
                "Reservation producer, Organization or correlation is invalid."
            );

        var command = Codec.Deserialize(
            message.MessageName,
            message.SchemaVersion,
            message.Payload
        );
        bool valid = command switch
        {
            ReserveStockV1 reserve => reserve.ReservationId != Guid.Empty
                && reserve.StockPositionId != Guid.Empty
                && reserve.ExpectedVersion >= 1
                && reserve.ExpectedVersion < long.MaxValue
                && reserve.Quantity > 0,
            ReleaseStockReservationV1 release => release.ReservationId != Guid.Empty
                && Guid.TryParseExact(message.CausationId, "D", out Guid cause)
                && cause != Guid.Empty,
            _ => false,
        };
        if (!valid)
            throw new InvalidDataException("Reservation command fields are invalid.");

        return command;
    }
}
