using System.Diagnostics;
using System.Text.Json;
using Gloaming.Events.Serialization;
using Gloaming.Messaging;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Inventory.StockPositions;

namespace Gloaming.Samples.Wholesale.Inventory.Messaging;

/// <summary>Consumer-owned mapping from an accepted stock issue to its versioned integration envelope.</summary>
/// <remarks>Chooses delivery identity, wire contract, routing and owner metadata; does not enqueue or publish.</remarks>
internal sealed class StockIssueMessages(
    InventoryDbContext database,
    InventoryMessageContext metadata
)
{
    // Direct commands start a conversation per operation scope. Inbox replies preserve
    // the admitted conversation, including its deliberate absence; an item ID is separate.
    private readonly string operationCorrelationId = Guid.CreateVersion7().ToString("D");
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonEventCodec<StockIssueRecordedV1> Codec = new(
        WireJson,
        [
            EventRegistration<StockIssueRecordedV1>.For<StockIssueRecordedV1>(
                "inventory.stock-issue-recorded",
                1
            ),
        ]
    );

    internal OutgoingMessage StockIssueRecorded(
        StockPositionAggregate aggregate,
        decimal issued,
        DateTimeOffset recordedAt
    )
    {
        Guid messageId = Guid.NewGuid();
        string owner = database.RequiredOrganizationKey;
        var contract = new StockIssueRecordedV1(
            messageId,
            owner,
            aggregate.Id,
            aggregate.Version,
            issued,
            aggregate.State!.OnHand,
            recordedAt
        );
        var encoded = Codec.Serialize(contract);
        return new(
            messageId,
            "inventory.stock-issues",
            encoded.EventName,
            encoded.SchemaVersion,
            encoded.Payload,
            owner,
            metadata.Message is { } incoming ? incoming.CorrelationId : operationCorrelationId,
            metadata.Message?.MessageId.ToString(),
            Activity.Current?.Id,
            Activity.Current?.TraceStateString
        );
    }

    internal OutgoingMessage StockIssueDeclined(
        IssueStockV1 command,
        StockIssueDeclineReason reason,
        decimal? available,
        decimal? requested
    )
    {
        Guid messageId = Guid.NewGuid();
        string owner = database.RequiredOrganizationKey;
        return OutgoingMessage.FromPayload(
            messageId,
            "inventory.stock-issues",
            "inventory.stock-issue-declined",
            1,
            new StockIssueDeclinedV1(
                messageId,
                owner,
                command.StockPositionId,
                command.ExpectedVersion,
                reason,
                available,
                requested
            ),
            WireJson,
            owner,
            metadata.Message is { } incoming ? incoming.CorrelationId : operationCorrelationId,
            metadata.Message?.MessageId.ToString(),
            Activity.Current?.Id,
            Activity.Current?.TraceStateString
        );
    }
}
