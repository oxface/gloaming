using Gloaming.EventSourcing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal sealed class PurchaseOrderAggregate
    : EventSourcedAggregate<PurchaseOrderState, IPurchaseOrderEvent>
{
    private PurchaseOrderAggregate(Guid id, long version, PurchaseOrderState? state)
        : base(id, version, state) { }

    internal static PurchaseOrderAggregate Create(DraftPurchaseOrder request)
    {
        var aggregate = new PurchaseOrderAggregate(request.Id, 0, null);
        aggregate.Draft(request);
        return aggregate;
    }

    internal static PurchaseOrderAggregate FromState(
        Guid id,
        long version,
        PurchaseOrderState state
    ) => new(id, version, state);

    internal void Draft(DraftPurchaseOrder request)
    {
        if (State is not null)
            throw new InvalidOperationException("The order is already drafted.");
        ApplyChanges(PurchaseOrderDecisions.Draft(request));
    }

    internal void SetLines(IReadOnlyList<PurchaseOrderLine> lines)
    {
        if (State is null)
            throw new InvalidOperationException("The order must be drafted.");
        ApplyChanges(PurchaseOrderDecisions.Lines(lines));
    }

    protected override void ValidateCandidate(PurchaseOrderState candidate) => _ = candidate.Total;

    protected override PurchaseOrderState Evolve(
        PurchaseOrderState? state,
        IReadOnlyList<IPurchaseOrderEvent> events
    ) => PurchaseOrderEvolution.Evolve(state, events);
}
