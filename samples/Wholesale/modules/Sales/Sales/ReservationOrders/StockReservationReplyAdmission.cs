using System.Text.Json;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using Rootbolt.Events.Serialization;
using Rootbolt.Messaging;

namespace ModulithFoundry.Samples.Wholesale.Sales;

/// <summary>Inventory reservation outcomes admitted to the Sales-owned process lane.</summary>
public static class StockReservationReplyAdmission
{
    public const string Subscription = "sales.reservation-outcomes";
    private static readonly JsonEventCodec<object> Codec = new(
        new JsonSerializerOptions(JsonSerializerDefaults.Web),
        [
            EventRegistration<object>.For<StockReservationDecidedV1>(
                "inventory.reservation-decided",
                1
            ),
            EventRegistration<object>.For<StockReservationReleasedV1>(
                "inventory.reservation-released",
                1
            ),
        ]
    );

    public static void Validate(IncomingMessage message) => _ = Decode(message);

    internal static object Decode(IncomingMessage message)
    {
        if (
            message.ProducerKey != "inventory"
            || message.TenantKey is not ("wholesale-alpha" or "wholesale-beta")
            || !Guid.TryParseExact(message.CorrelationId, "D", out Guid orderId)
            || orderId == Guid.Empty
            || !Guid.TryParseExact(message.CausationId, "D", out Guid commandId)
            || commandId == Guid.Empty
        )
            throw new InvalidDataException("Reservation outcome metadata is invalid.");

        var outcome = Codec.Deserialize(
            message.MessageName,
            message.SchemaVersion,
            message.Payload
        );
        bool valid = outcome switch
        {
            StockReservationDecidedV1 reserve => reserve.ReservationId != Guid.Empty
                && reserve.StockPositionId != Guid.Empty
                && reserve.ExpectedVersion >= 1
                && reserve.Quantity > 0
                && Enum.IsDefined(reserve.Decision),
            StockReservationReleasedV1 release => release.ReservationId != Guid.Empty
                && Enum.IsDefined(release.Decision),
            _ => false,
        };
        if (!valid)
            throw new InvalidDataException("Reservation outcome fields are invalid.");

        return outcome;
    }
}
