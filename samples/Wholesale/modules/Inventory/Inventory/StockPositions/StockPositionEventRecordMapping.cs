using Gloaming.Events.Serialization;
using Gloaming.EventSourcing.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Inventory.StockPositions;

internal sealed class StockPositionEventRecordMapping
    : EventRecordMapping<IStockPositionEvent, EventStream, StoredEvent>
{
    private readonly JsonEventCodec<IStockPositionEvent> codec = StockPositionCodec.CreateCodec();
    public override string StreamType => StockPositionHistoryReader.StreamType;

    public override StoredEvent ToRow(IStockPositionEvent @event, EventStream stream)
    {
        var encoded = codec.Serialize(@event);
        return new StoredEvent
        {
            OrganizationKey = stream.OrganizationKey,
            EventName = encoded.EventName,
            SchemaVersion = encoded.SchemaVersion,
            Payload = encoded.Payload,
        };
    }
}
