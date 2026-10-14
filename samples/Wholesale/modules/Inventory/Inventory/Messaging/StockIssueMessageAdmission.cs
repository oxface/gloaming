using System.Text.Json;
using Gloaming.Events.Serialization;
using Gloaming.Messaging;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Inventory;

/// <summary>Finite sample producer/Organization policy and explicit integration codec; not sender authentication.</summary>
public static class StockIssueMessageAdmission
{
    public const string Subscription = "inventory.issue-stock";
    public const string Producer = "demo.stock-commands";
    public const string SalesProducer = "sales";
    private static readonly JsonEventCodec<IssueStockV1> Codec = new(
        new JsonSerializerOptions(JsonSerializerDefaults.Web),
        [EventRegistration<IssueStockV1>.For<IssueStockV1>(Subscription, 1)]
    );

    public static IssueStockV1 Validate(IncomingMessage message)
    {
        // The host owns broker permissions and binds the producer identity. This finite sample
        // allows two known Organizations; production replaces this with trusted admission.
        if (
            message.ProducerKey is not (Producer or SalesProducer)
            || message.TenantKey is not ("wholesale-alpha" or "wholesale-beta")
        )
            throw new InvalidDataException(
                "The stock command producer/Organization is not admitted."
            );

        var command = Codec.Deserialize(
            message.MessageName,
            message.SchemaVersion,
            message.Payload
        );
        if (
            command.StockPositionId == Guid.Empty
            || command.ExpectedVersion < 1
            || command.Quantity <= 0
        )
            throw new InvalidDataException(
                "The stock command needs an identity, observed version and positive quantity."
            );

        return command;
    }
}
