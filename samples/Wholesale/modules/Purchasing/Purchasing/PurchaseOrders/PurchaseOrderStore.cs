using Gloaming.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal sealed class PurchaseOrderStore
    : EventStore<PurchaseOrderAggregate, IPurchaseOrderEvent, EventStream, StoredEvent>
{
    private readonly PurchasingDbContext database;

    public PurchaseOrderStore(PurchasingDbContext database, TimeProvider timeProvider)
        : base(database, new PurchaseOrderEventRecordMapping(), timeProvider)
    {
        this.database = database;
        ConfigureInlineState(new PurchaseOrderStateMapping());
    }

    protected override EventStream CreateStream(Guid id) =>
        new() { OrganizationKey = database.RequiredOrganizationKey };
}
