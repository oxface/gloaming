using Gloaming.EventSourcing;

namespace ModulithFoundry.Samples.EventStorageDemo;

internal sealed class CounterAggregate : EventSourcedAggregate<CounterValue, CounterEvent>
{
    private const int Limit = 25;

    private CounterAggregate(Guid id, long version, CounterValue? state)
        : base(id, version, state) { }

    internal static CounterAggregate Create(Guid id, int value)
    {
        var aggregate = new CounterAggregate(id, 0, null);
        aggregate.Start(value);
        return aggregate;
    }

    internal static CounterAggregate FromState(Guid id, long version, int value) =>
        new(id, version, new CounterValue(value));

    internal void Start(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Limit);
        if (State is not null)
            throw new InvalidOperationException("The counter has already started.");
        ApplyChanges([new CounterStarted(value)]);
    }

    internal bool TryIncrease(int[] amounts, out int requested)
    {
        if (State is null)
            throw new InvalidOperationException("The counter must be started.");
        requested = amounts.Aggregate(0, (total, amount) => checked(total + amount));
        if (checked(State.Value + requested) > Limit)
            return false;
        ApplyChanges(
            amounts.Select(amount => (CounterEvent)new CounterIncreased(amount)).ToArray()
        );
        return true;
    }

    protected override CounterValue Evolve(
        CounterValue? state,
        IReadOnlyList<CounterEvent> events
    ) => CounterEvolution.Evolve(state, events);

    protected override void ValidateCandidate(CounterValue candidate)
    {
        if (candidate.Value > Limit)
            throw new InvalidOperationException("The counter candidate exceeds its ceiling.");
    }
}
