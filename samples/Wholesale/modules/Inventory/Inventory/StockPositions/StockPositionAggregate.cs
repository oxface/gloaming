using Gloaming.EventSourcing;
using Gloaming.Samples.Wholesale.Inventory.Contracts;

namespace Gloaming.Samples.Wholesale.Inventory.StockPositions;

internal sealed class StockPositionAggregate
    : EventSourcedAggregate<StockPositionState, IStockPositionEvent>
{
    private StockPositionAggregate(Guid id, long version, StockPositionState? state)
        : base(id, version, state) { }

    internal static StockPositionAggregate Create(OpenStockPosition request)
    {
        var aggregate = new StockPositionAggregate(request.Id, 0, null);
        aggregate.Open(request);
        return aggregate;
    }

    internal static StockPositionAggregate FromState(
        Guid id,
        long version,
        StockPositionState state
    ) => new(id, version, state);

    internal void Open(OpenStockPosition request)
    {
        if (State is not null)
            throw new InvalidOperationException("The stock position is already open.");
        ApplyChanges(StockPositionDecisions.Open(request));
    }

    internal void Receive(IReadOnlyList<StockReceipt> receipts)
    {
        if (State is null)
            throw new InvalidOperationException("The stock position must be open.");
        ApplyChanges(StockPositionDecisions.Receipts(receipts));
    }

    internal bool TryIssue(decimal[] quantities, out decimal requested)
    {
        var decision = StockPositionDecisions.Issues(
            State ?? throw new InvalidOperationException("The stock position must be open."),
            quantities
        );
        requested = decision.Requested;
        if (decision.Events is null)
            return false;
        ApplyChanges(decision.Events);
        return true;
    }

    protected override void ValidateCandidate(StockPositionState candidate)
    {
        if (candidate.OnHand < 0 || candidate.Reserved < 0 || candidate.Available < 0)
            throw new InvalidOperationException(
                "The complete stock candidate has negative on-hand quantity."
            );
    }

    internal bool TryReserve(Guid reservationId, decimal quantity)
    {
        if (State is null)
            throw new InvalidOperationException("The stock position must be open.");

        if (quantity > State.Available)
            return false;

        ApplyChanges([new StockPositionReserved(reservationId, quantity)]);
        return true;
    }

    internal void Release(Guid reservationId, decimal quantity) =>
        ApplyChanges([new StockPositionReservationReleased(reservationId, quantity)]);

    protected override StockPositionState Evolve(
        StockPositionState? state,
        IReadOnlyList<IStockPositionEvent> events
    ) => StockPositionEvolution.Evolve(state, events);
}
