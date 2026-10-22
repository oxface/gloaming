using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.EventStorageDemo;

public sealed class CounterCommands(StorageDbContext database, TimeProvider timeProvider)
{
    private readonly CounterStore store = new(database, timeProvider);

    public async Task<CounterChangeResult> StageStartAsync(
        Guid id,
        int value,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 25);
        try
        {
            _ = await store.GetForWritingAsync(id, 0, cancellationToken);
            var aggregate = CounterAggregate.Create(id, value);
            var time = (await store.AppendAsync(aggregate, cancellationToken)).RecordedAt;
            return new CounterChangeResult.Changed(
                new(id, aggregate.Version, aggregate.State!.Value, time)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return new CounterChangeResult.Conflict();
        }
    }

    public async Task<CounterChangeResult> StageIncreaseAsync(
        Guid id,
        long expectedVersion,
        IReadOnlyList<int> amounts,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 1);
        ArgumentNullException.ThrowIfNull(amounts);
        int[] batch = amounts.ToArray();
        ArgumentOutOfRangeException.ThrowIfLessThan(batch.Length, 1);
        foreach (int amount in batch)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        try
        {
            var aggregate = await store.GetForWritingAsync(id, expectedVersion, cancellationToken);
            if (aggregate is null)
                return new CounterChangeResult.NotFound();
            if (!aggregate.TryIncrease(batch, out int requested))
                return new CounterChangeResult.LimitExceeded(aggregate.State!.Value, requested);
            var time = (await store.AppendAsync(aggregate, cancellationToken)).RecordedAt;
            return new CounterChangeResult.Changed(
                new(id, aggregate.Version, aggregate.State!.Value, time)
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            return new CounterChangeResult.Conflict();
        }
    }

    public Task<CounterState?> ReadAsync(Guid id, CancellationToken cancellationToken) =>
        store.ReadAsync(id, cancellationToken);
}

public sealed record CounterState(Guid Id, long Version, int Value, DateTimeOffset RecordedAt);

public abstract record CounterChangeResult
{
    public sealed record Changed(CounterState Proposed) : CounterChangeResult;

    public sealed record LimitExceeded(int Value, int Requested) : CounterChangeResult;

    public sealed record NotFound : CounterChangeResult;

    public sealed record Conflict : CounterChangeResult;
}
