namespace Gloaming.EventReactions;

public sealed class EventReactionDispatcher<TModule>
{
    private readonly EventReactionRegistry<TModule> _registry;
    private readonly PendingEvents _sink;
    private readonly int _maxInvocations;
    private bool _executing;
    private bool _failed;

    public EventReactionDispatcher(
        EventReactionRegistry<TModule> registry,
        PendingEvents sink,
        int maxReactionInvocations = 128
    )
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxReactionInvocations);

        _registry = registry;
        _sink = sink;
        _maxInvocations = maxReactionInvocations;
        registry.Freeze();
    }

    public IEventSink Sink => _sink;
    public int PendingCount => _sink.Count;

    public void Dispatch(Func<IEnumerable<PendingEvents>> collectSources)
    {
        ArgumentNullException.ThrowIfNull(collectSources);

        Enter();
        try
        {
            int invocations = 0;
            while (true)
            {
                var batch = Collect(collectSources);
                CheckHealthy();
                if (batch.Length == 0)
                    return;

                foreach (var fact in batch)
                {
                    foreach (var handler in _registry.For(fact))
                    {
                        if (handler.Sync is null)
                            throw new InvalidOperationException(
                                "An async-only handler requires asynchronous saving."
                            );

                        Count(ref invocations);
                        handler.Sync(fact);
                        CheckHealthy();
                    }
                }
            }
        }
        catch
        {
            _failed = true;
            throw;
        }
        finally
        {
            _executing = false;
        }
    }

    public async Task DispatchAsync(
        Func<IEnumerable<PendingEvents>> collectSources,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(collectSources);

        Enter();
        try
        {
            int invocations = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = Collect(collectSources);
                CheckHealthy();
                if (batch.Length == 0)
                    return;

                foreach (var fact in batch)
                {
                    foreach (var handler in _registry.For(fact))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Count(ref invocations);
                        await handler.Async(fact, cancellationToken);
                        CheckHealthy();
                    }
                }
            }
        }
        catch
        {
            _failed = true;
            throw;
        }
        finally
        {
            _executing = false;
        }
    }

    private object[] Collect(Func<IEnumerable<PendingEvents>> collectSources) =>
        new[] { _sink }
            .Concat(collectSources())
            .Distinct()
            .SelectMany(source => source.TakeAll())
            .OrderBy(occurrence => occurrence.Sequence)
            .Select(occurrence => occurrence.Fact)
            .ToArray();

    private void Enter()
    {
        if (_executing || _failed)
        {
            _failed = true;
            throw new InvalidOperationException(
                "Discard a failed dispatcher; nested dispatch is unsupported."
            );
        }

        _executing = true;
    }

    private void Count(ref int invocations)
    {
        if (invocations >= _maxInvocations)
            throw new InvalidOperationException(
                "The handler invocation ceiling was exceeded; roll back the operation."
            );

        invocations++;
    }

    private void CheckHealthy()
    {
        if (_failed)
            throw new InvalidOperationException("A nested dispatch invalidated the operation.");
    }
}
