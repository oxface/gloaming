using Gloaming.EventSourcing.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Inventory.StockPositions;

internal sealed class StockPositionStore
    : EventStore<StockPositionAggregate, IStockPositionEvent, EventStream, StoredEvent>
{
    private readonly InventoryDbContext database;

    public StockPositionStore(InventoryDbContext database, TimeProvider timeProvider)
        : base(database, new StockPositionEventRecordMapping(), timeProvider)
    {
        this.database = database;
        ConfigureInlineState(new StockPositionStateMapping());
    }

    protected override EventStream CreateStream(Guid id) =>
        new() { OrganizationKey = database.RequiredOrganizationKey };
}
