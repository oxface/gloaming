using Gloaming.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal sealed class PurchaseOrderRebuilder(
    PurchasingDbContext database,
    PurchaseOrderHistoryReader historyReader
)
    : AggregateRebuilder<
        PurchaseOrderAggregate,
        IPurchaseOrderEvent,
        EventStream,
        PurchaseOrderStateRow
    >(
        database,
        PurchaseOrderHistoryReader.StreamType,
        new PurchaseOrderStateMapping(),
        historyReader
    )
{
    // Historical evolution reconstructs state without pending facts or today's command eligibility rules.
    protected override PurchaseOrderAggregate Rehydrate(
        EventStream observedStream,
        IReadOnlyList<IPurchaseOrderEvent> events
    ) =>
        PurchaseOrderAggregate.FromState(
            observedStream.Id,
            observedStream.Version,
            PurchaseOrderEvolution.Evolve(null, events)
        );
}
