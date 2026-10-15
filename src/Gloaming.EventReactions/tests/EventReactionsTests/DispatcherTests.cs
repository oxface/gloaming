using Gloaming.EventReactions;

namespace Gloaming.EventReactions.Tests;

public sealed class DispatcherTests
{
    private sealed class SalesModule;

    private sealed class OtherModule;

    private sealed record Submitted(int Id);

    private sealed record Allocated(int Id);

    private readonly record struct Imported(int Count);

    [Fact]
    public void OrdinaryValueRecordsNeedNoEventMarkerOrBaseClass()
    {
        int imported = 0;
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Imported>(new Handler<Imported>(fact => imported += fact.Count));
        var pending = new PendingEvents();
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, pending);
        pending.Raise(new Imported(3));
        pending.Raise(new Imported(4));
        dispatcher.Dispatch(() => []);
        Assert.Equal(7, imported);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrderedHandlersDrainCascadesAndNewSourcesBeforeReturning(bool asynchronous)
    {
        var trace = new List<string>();
        var sink = new PendingEvents();
        var sources = new List<PendingEvents>();
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(
            new Handler<Submitted>(fact =>
            {
                trace.Add("reserve-" + fact.Id);
                var recipient = new PendingEvents();
                recipient.Raise(new Allocated(fact.Id));
                sources.Add(recipient);
            })
        );
        registry.Add<Submitted>(new Handler<Submitted>(fact => trace.Add("receipt-" + fact.Id)));
        registry.Add<Allocated>(new Handler<Allocated>(fact => trace.Add("allocation-" + fact.Id)));
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, sink, 6);
        sink.Raise(new Submitted(1));
        sink.Raise(new Submitted(2));
        if (asynchronous)
            await dispatcher.DispatchAsync(() => sources, TestContext.Current.CancellationToken);
        else
            dispatcher.Dispatch(() => sources);

        Assert.Equal(
            ["reserve-1", "receipt-1", "reserve-2", "receipt-2", "allocation-1", "allocation-2"],
            trace
        );
        Assert.Equal(0, sink.Count);
        Assert.All(sources, source => Assert.Equal(0, source.Count));
        dispatcher.Dispatch(() => sources);
        Assert.Equal(6, trace.Count);
        sink.Raise(new Submitted(3));
        dispatcher.Dispatch(() => sources);
        Assert.Equal(["reserve-3", "receipt-3", "allocation-3"], trace.TakeLast(3));
    }

    [Fact]
    public void EqualValuesAreDistinctOccurrencesButTheSameSourceIsCollectedOnce()
    {
        int calls = 0;
        var source = new PendingEvents();
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(new Handler<Submitted>(_ => calls++));
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, source);
        var fact = new Submitted(1);
        source.Raise(fact);
        source.Raise(fact);
        dispatcher.Dispatch(() => [source, source]);
        Assert.Equal(2, calls);
        Assert.Equal(0, source.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RaiseOrderAcrossAggregatesAndOperationSinkSurvivesReversedSourceCollection(
        bool asynchronous
    )
    {
        var trace = new List<int>();
        var sink = new PendingEvents();
        var first = new PendingEvents();
        var second = new PendingEvents();
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(new Handler<Submitted>(fact => trace.Add(fact.Id)));
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, sink);
        first.Raise(new Submitted(1));
        second.Raise(new Submitted(2));
        first.Raise(new Submitted(3));
        sink.Raise(new Submitted(4));
        second.Raise(new Submitted(5));

        if (asynchronous)
            await dispatcher.DispatchAsync(
                () => [second, first, second],
                TestContext.Current.CancellationToken
            );
        else
            dispatcher.Dispatch(() => [second, first, second]);

        Assert.Equal([1, 2, 3, 4, 5], trace);
        Assert.Equal(0, first.Count);
        Assert.Equal(0, second.Count);
        Assert.Equal(0, sink.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownstreamEventsPreserveCaptureOrderAcrossNewSourcesAndTheSink(
        bool asynchronous
    )
    {
        var trace = new List<string>();
        var sink = new PendingEvents();
        var sources = new List<PendingEvents>();
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(
            new Handler<Submitted>(fact =>
            {
                trace.Add("submitted-" + fact.Id);
                var first = new PendingEvents();
                var second = new PendingEvents();
                first.Raise(new Allocated(fact.Id * 10 + 1));
                sink.Raise(new Allocated(fact.Id * 10 + 2));
                second.Raise(new Allocated(fact.Id * 10 + 3));
                // Neither source enumeration nor sink priority represents capture order.
                sources.Insert(0, first);
                sources.Insert(0, second);
            })
        );
        registry.Add<Allocated>(new Handler<Allocated>(fact => trace.Add("allocated-" + fact.Id)));
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, sink);
        sink.Raise(new Submitted(1));
        sink.Raise(new Submitted(2));

        if (asynchronous)
            await dispatcher.DispatchAsync(() => sources, TestContext.Current.CancellationToken);
        else
            dispatcher.Dispatch(() => sources);

        Assert.Equal(
            [
                "submitted-1",
                "submitted-2",
                "allocated-11",
                "allocated-12",
                "allocated-13",
                "allocated-21",
                "allocated-22",
                "allocated-23",
            ],
            trace
        );
    }

    [Fact]
    public void ModuleRegistriesAreIsolatedForTheSamePublicFact()
    {
        int firstCalls = 0,
            secondCalls = 0;
        var firstRegistry = new EventReactionRegistry<SalesModule>();
        var secondRegistry = new EventReactionRegistry<OtherModule>();
        firstRegistry.Add<Submitted>(new Handler<Submitted>(_ => firstCalls++));
        secondRegistry.Add<Submitted>(new Handler<Submitted>(_ => secondCalls++));
        var first = new EventReactionDispatcher<SalesModule>(firstRegistry, new());
        var second = new EventReactionDispatcher<OtherModule>(secondRegistry, new());
        first.Sink.Raise(new Submitted(1));
        first.Dispatch(() => []);
        Assert.Equal(1, firstCalls);
        Assert.Equal(0, secondCalls);
        second.Sink.Raise(new Submitted(2));
        second.Dispatch(() => []);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public void RegistrationsAreFrozenAndMissingRequiredHandlersFail()
    {
        var registry = new EventReactionRegistry<SalesModule>();
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, new());
        Assert.Throws<InvalidOperationException>(() =>
            registry.Add<Submitted>(new Handler<Submitted>(_ => { }))
        );
        dispatcher.Sink.Raise(new Submitted(1));
        Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(() => []));
        Assert.Equal(0, dispatcher.PendingCount);
        Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(() => []));
    }

    [Fact]
    public void DuplicateHandlerInstancesAreRejected()
    {
        var handler = new Handler<Submitted>(_ => { });
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(handler);
        Assert.Throws<InvalidOperationException>(() => registry.Add<Submitted>(handler));
    }

    [Fact]
    public void EachBatchIsClearedBeforeHandlersAndFailureRequiresFreshState()
    {
        var source = new PendingEvents();
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(
            new Handler<Submitted>(_ =>
            {
                Assert.Equal(0, source.Count);
                throw new InvalidOperationException("reject");
            })
        );
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, source);
        source.Raise(new Submitted(1));
        source.Raise(new Submitted(2));
        Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(() => []));
        Assert.Equal(0, source.Count);
        Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(() => []));
    }

    [Fact]
    public void CascadingCycleStopsAtTheInvocationCeiling()
    {
        int calls = 0;
        var sink = new PendingEvents();
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(
            new Handler<Submitted>(fact =>
            {
                calls++;
                sink.Raise(fact);
            })
        );
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, sink, 3);
        sink.Raise(new Submitted(1));
        Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(() => []));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void CompletionAtTheExactInvocationLimitSucceeds()
    {
        var registry = new EventReactionRegistry<SalesModule>();
        int calls = 0;
        registry.Add<Submitted>(new Handler<Submitted>(_ => calls++));
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, new(), 1);
        dispatcher.Sink.Raise(new Submitted(1));
        dispatcher.Dispatch(() => []);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void SwallowingNestedDispatchDoesNotAllowOuterSuccess()
    {
        EventReactionDispatcher<SalesModule>? dispatcher = null;
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(
            new Handler<Submitted>(_ =>
                Assert.Throws<InvalidOperationException>(() => dispatcher!.Dispatch(() => []))
            )
        );
        dispatcher = new(registry, new());
        dispatcher.Sink.Raise(new Submitted(1));
        Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(() => []));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SwallowingNestedDispatchDuringCollectionRejectsOuterDispatchBeforeHandling(
        bool asynchronous,
        bool hasPendingEvents
    )
    {
        int calls = 0;
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(new Handler<Submitted>(_ => calls++));
        var dispatcher = new EventReactionDispatcher<SalesModule>(registry, new());
        if (hasPendingEvents)
            dispatcher.Sink.Raise(new Submitted(1));

        IEnumerable<PendingEvents> Collect()
        {
            Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(() => []));
            return [];
        }

        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                dispatcher.DispatchAsync(Collect, TestContext.Current.CancellationToken)
            );
        else
            Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(Collect));

        Assert.Equal(0, calls);
        Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(() => []));
    }

    [Fact]
    public async Task AsyncOnlyHandlerRunsAsynchronouslyAndSyncDispatchRejectsIt()
    {
        int calls = 0;
        var registry = new EventReactionRegistry<SalesModule>();
        registry.Add<Submitted>(
            new AsyncOnly(_ =>
            {
                calls++;
                return Task.CompletedTask;
            })
        );
        var sync = new EventReactionDispatcher<SalesModule>(registry, new());
        sync.Sink.Raise(new Submitted(1));
        Assert.Throws<InvalidOperationException>(() => sync.Dispatch(() => []));
        Assert.Equal(0, calls);
        var async = new EventReactionDispatcher<SalesModule>(registry, new());
        async.Sink.Raise(new Submitted(1));
        await async.DispatchAsync(() => [], TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationIsObservedBeforeCollectingOrCallingHandlers()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sink = new PendingEvents();
        sink.Raise(new Submitted(1));
        var dispatcher = new EventReactionDispatcher<SalesModule>(new(), sink);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(() => [], cancellation.Token)
        );
        Assert.Equal(1, sink.Count);
    }

    private sealed class Handler<T>(Action<T> handle)
        : IEventReaction<T>,
            ISynchronousEventReaction<T>
        where T : notnull
    {
        public void Handle(T fact) => handle(fact);

        public Task HandleAsync(T fact, CancellationToken cancellationToken)
        {
            handle(fact);
            return Task.CompletedTask;
        }
    }

    private sealed class AsyncOnly(Func<Submitted, Task> handle) : IEventReaction<Submitted>
    {
        public Task HandleAsync(Submitted fact, CancellationToken cancellationToken) =>
            handle(fact);
    }
}
