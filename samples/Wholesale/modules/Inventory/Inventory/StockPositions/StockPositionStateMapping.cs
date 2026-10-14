using Gloaming.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

internal sealed class StockPositionStateMapping
    : AggregateStateMapping<StockPositionAggregate, StockPositionStateRow>
{
    public override StockPositionAggregate ToAggregate(StockPositionStateRow stateRow) =>
        StockPositionAggregate.FromState(stateRow.StreamId, stateRow.Version, stateRow.ReadState());

    public override StockPositionStateRow ToRow(StockPositionAggregate aggregate) =>
        StockPositionStateRow.FromState(aggregate.State!);
}
