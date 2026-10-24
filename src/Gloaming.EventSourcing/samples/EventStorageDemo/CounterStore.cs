using Gloaming.Events.History;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.EventStorageDemo;

internal sealed class CounterStore
    : EventStore<CounterAggregate, CounterEvent, EventStreamRecord, StoredEventRecord>
{
    private readonly StorageDbContext database;
    private readonly CounterHistoryReader historyReader;

    internal CounterStore(StorageDbContext database, TimeProvider timeProvider)
        : base(database, new CounterEventRecordMapping(), timeProvider)
    {
        this.database = database;
        historyReader = new(database);
    }

    protected override EventStreamRecord CreateStream(Guid id) =>
        new() { Description = "Append counter" };

    protected override async Task<CounterAggregate> LoadAggregateAsync(
        EventStreamRecord stream,
        CancellationToken cancellationToken
    )
    {
        var state = await LoadStateAsync(stream, cancellationToken);
        return CounterAggregate.FromState(stream.Id, stream.Version, state.Value);
    }

    internal async Task<CounterState?> ReadAsync(Guid id, CancellationToken cancellationToken) =>
        (await LoadAsync(id, cancellationToken)).State;

    private async Task<(EventStreamRecord? Stream, CounterState? State)> LoadAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var stream = await database
            .Streams.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (stream is null)
            return (null, null);
        return (stream, await LoadStateAsync(stream, cancellationToken));
    }

    private async Task<CounterState> LoadStateAsync(
        EventStreamRecord stream,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<ReplayedEvent<CounterEvent>> events;
        try
        {
            events = await historyReader.ReadAsync(stream, cancellationToken: cancellationToken);
        }
        catch (EventHistoryException exception)
        {
            // Keep the counter's established public corruption classification.
            throw new InvalidDataException("The counter prefix is invalid.", exception);
        }

        // Historical facts do not re-run today's ceiling policy.
        return new CounterState(
            stream.Id,
            stream.Version,
            CounterEvolution.Evolve(null, events.Select(item => item.Event).ToArray()).Value,
            stream.UpdatedAt
        );
    }
}
