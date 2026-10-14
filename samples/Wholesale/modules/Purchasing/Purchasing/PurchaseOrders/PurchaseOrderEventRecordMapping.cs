using Gloaming.Events.Serialization;
using Gloaming.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal sealed class PurchaseOrderEventRecordMapping
    : EventRecordMapping<IPurchaseOrderEvent, EventStream, StoredEvent>
{
    private readonly JsonEventCodec<IPurchaseOrderEvent> codec = PurchaseOrderCodec.CreateCodec();
    public override string StreamType => PurchaseOrderHistoryReader.StreamType;

    public override StoredEvent ToRow(IPurchaseOrderEvent @event, EventStream stream)
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
