namespace ModulithFoundry.Samples.EventStorageDemo;

internal abstract record CounterEvent;

internal sealed record CounterStarted(int Value) : CounterEvent;

internal sealed record CounterIncreased(int Amount) : CounterEvent;

internal sealed record CounterValue(int Value);

internal static class CounterEvolution
{
    internal static CounterValue Evolve(CounterValue? state, IReadOnlyList<CounterEvent> events)
    {
        foreach (var @event in events)
            state = @event switch
            {
                CounterStarted started when state is null => new(started.Value),
                CounterIncreased increased when state is not null => new(
                    checked(state.Value + increased.Amount)
                ),
                _ => throw new InvalidDataException("The counter fact sequence is invalid."),
            };
        return state ?? throw new InvalidDataException("The counter has not started.");
    }
}
