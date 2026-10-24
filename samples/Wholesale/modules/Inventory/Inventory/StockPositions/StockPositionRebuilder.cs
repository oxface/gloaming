using Gloaming.EventSourcing.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Inventory.StockPositions;

internal sealed class StockPositionRebuilder(
    InventoryDbContext database,
    StockPositionHistoryReader historyReader
)
    : AggregateRebuilder<
        StockPositionAggregate,
        IStockPositionEvent,
        EventStream,
        StockPositionStateRow
    >(
        database,
        StockPositionHistoryReader.StreamType,
        new StockPositionStateMapping(),
        historyReader
    )
{
    // Historical evolution reconstructs state without pending facts or today's command eligibility rules.
    protected override StockPositionAggregate Rehydrate(
        EventStream observedStream,
        IReadOnlyList<IStockPositionEvent> events
    ) =>
        StockPositionAggregate.FromState(
            observedStream.Id,
            observedStream.Version,
            StockPositionEvolution.Evolve(null, events)
        );
}
