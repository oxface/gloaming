using System.Diagnostics.CodeAnalysis;
using Gloaming.ActorIdentity;

namespace Gloaming.ActorIdentityTests;

[SuppressMessage(
    "Performance",
    "CA1859:Use concrete types when possible",
    Justification = "These tests exercise the public reader and initializer interfaces."
)]
public sealed class AccessorTests
{
    [Fact]
    public void UnestablishedContextNeverFallsBackToAnonymous()
    {
        using var holder = new ActorContextAccessor();
        IActorContextAccessor reader = holder;
        IActorContextInitializer initializer = holder;

        Assert.Throws<InvalidOperationException>(() => reader.Current);
        Assert.Throws<ArgumentNullException>(() => initializer.Initialize(null!));
        Assert.Throws<InvalidOperationException>(() => reader.Current);

        ActorContext deliberate = new ActorContext(Actor.Anonymous);
        initializer.Initialize(deliberate);
        Assert.Same(deliberate, reader.Current);
    }

    [Fact]
    public void SameOrDifferentContextCannotReplaceTheEstablishedValue()
    {
        using var holder = new ActorContextAccessor();
        IActorContextAccessor reader = holder;
        IActorContextInitializer initializer = holder;
        ActorContext first = new ActorContext(Actor.Anonymous);
        initializer.Initialize(first);

        Assert.Throws<InvalidOperationException>(() => initializer.Initialize(first));
        Assert.Throws<InvalidOperationException>(() =>
            initializer.Initialize(new ActorContext(Actor.System(new ActorId("workflow"))))
        );
        Assert.Same(first, reader.Current);
    }

    [Fact]
    public async Task CompetingInitializersPublishExactlyOneCompleteContext()
    {
        using var holder = new ActorContextAccessor();
        IActorContextAccessor reader = holder;
        IActorContextInitializer initializer = holder;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ActorContext alpha = new ActorContext(
            Actor.System(new ActorId("alpha-workflow")),
            Actor.Human(new ActorId("alpha-user"))
        );
        ActorContext beta = new ActorContext(
            Actor.System(new ActorId("beta-workflow")),
            Actor.Human(new ActorId("beta-user"))
        );

        Task<ActorContext?>[] attempts = [AttemptAsync(alpha), AttemptAsync(beta)];
        start.SetResult();
        ActorContext?[] results = await Task.WhenAll(attempts)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        ActorContext winner = Assert.Single(results.OfType<ActorContext>());
        Assert.Single(results, result => result is null);
        Assert.Same(winner, reader.Current);
        Assert.Equal(winner.Actor, reader.Current.Actor);
        Assert.Equal(winner.Initiator, reader.Current.Initiator);

        async Task<ActorContext?> AttemptAsync(ActorContext context)
        {
            await start.Task.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                initializer.Initialize(context);
                return context;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    [Fact]
    public async Task ParallelReadersObserveTheSameCompleteImmutableValue()
    {
        using var holder = new ActorContextAccessor();
        IActorContextAccessor reader = holder;
        ActorContext expected = new ActorContext(
            Actor.System(new ActorId("workflow")),
            Actor.Human(new ActorId("initiator"))
        );
        holder.Initialize(expected);

        Task[] reads = Enumerable
            .Range(0, 16)
            .Select(_ =>
                Task.Run(
                    () =>
                    {
                        ActorContext actual = reader.Current;
                        Assert.Same(expected, actual);
                        Assert.Equal(ActorKind.System, actual.RequireIdentifiedActor().Kind);
                        Assert.Equal("initiator", actual.Initiator!.Id!.Value);
                    },
                    TestContext.Current.CancellationToken
                )
            )
            .ToArray();
        await Task.WhenAll(reads)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposalIsIdempotentAndPreventsReadsOrReinitialization(bool initialize)
    {
        var holder = new ActorContextAccessor();
        IActorContextAccessor reader = holder;
        IActorContextInitializer initializer = holder;
        ActorContext context = new ActorContext(Actor.Anonymous);
        if (initialize)
        {
            initializer.Initialize(context);
        }

        holder.Dispose();
        holder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reader.Current);
        Assert.Throws<ObjectDisposedException>(() => initializer.Initialize(context));
        Assert.Same(Actor.Anonymous, context.Actor);
    }
}
