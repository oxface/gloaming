using Gloaming.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal sealed class PurchaseOrderStateMapping
    : AggregateStateMapping<PurchaseOrderAggregate, PurchaseOrderStateRow>
{
    public override PurchaseOrderAggregate ToAggregate(PurchaseOrderStateRow stateRow) =>
        PurchaseOrderAggregate.FromState(stateRow.StreamId, stateRow.Version, stateRow.ReadState());

    public override PurchaseOrderStateRow ToRow(PurchaseOrderAggregate aggregate) =>
        PurchaseOrderStateRow.FromState(aggregate.State!);
}
