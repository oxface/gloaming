using System.Text.Json;
using Gloaming.Events.Serialization;
using Gloaming.Messaging;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Sales.Contracts;

namespace Gloaming.Samples.Wholesale.Sales;

/// <summary>Finite sample wire/Organization policy. The host binds Inventory identity; headers alone are not authentication.</summary>
public static class StockIssueReplyAdmission
{
    public const string Subscription = "sales.stock-issue-replies";
    public const string Producer = "inventory";
    private static readonly JsonEventCodec<object> Codec = new(
        new JsonSerializerOptions(JsonSerializerDefaults.Web),
        [
            EventRegistration<object>.For<StockIssueRecordedV1>(
                "inventory.stock-issue-recorded",
                1
            ),
            EventRegistration<object>.For<StockIssueDeclinedV1>(
                "inventory.stock-issue-declined",
                1
            ),
        ]
    );

    /// <summary>Rejects invalid metadata and payload shape before committed intake.</summary>
    public static void Validate(IncomingMessage message) => _ = Decode(message);

    internal static object Decode(IncomingMessage message)
    {
        if (
            message.ProducerKey != Producer
            || message.TenantKey is not ("wholesale-alpha" or "wholesale-beta")
            || !Guid.TryParseExact(message.CorrelationId, "D", out Guid requestId)
            || requestId == Guid.Empty
            || !Guid.TryParseExact(message.CausationId, "D", out Guid commandId)
            || commandId == Guid.Empty
        )
            throw new InvalidDataException(
                "The stock reply producer, Organization or operation identity is invalid."
            );

        var decoded = Codec.Deserialize(
            message.MessageName,
            message.SchemaVersion,
            message.Payload
        );
        bool valid = decoded switch
        {
            StockIssueRecordedV1 reply => reply.MessageId == message.MessageId
                && reply.OrganizationKey == message.TenantKey
                && reply.StockPositionId != Guid.Empty
                && reply.Version > 1
                && reply.IssuedQuantity > 0
                && reply.RemainingQuantity >= 0
                && reply.RecordedAt != default,
            StockIssueDeclinedV1 reply => reply.MessageId == message.MessageId
                && reply.OrganizationKey == message.TenantKey
                && reply.StockPositionId != Guid.Empty
                && reply.ExpectedVersion >= 1
                && Enum.IsDefined(reply.Reason)
                && (
                    reply.Reason == StockIssueDeclineReason.InsufficientStock
                        ? reply.Available >= 0 && reply.Requested > reply.Available
                        : reply.Available is null && reply.Requested is null
                ),
            _ => false,
        };
        if (!valid)
            throw new InvalidDataException("The stock reply payload is invalid.");

        return decoded;
    }
}
