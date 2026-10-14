using System.Diagnostics.CodeAnalysis;
using Gloaming.Tenancy;

namespace Gloaming.TenantTests;

[SuppressMessage(
    "Performance",
    "CA1859:Use concrete types when possible",
    Justification = "These tests exercise the public reader and initializer interfaces."
)]
public sealed class AccessorTests
{
    [Fact]
    public void UnestablishedContextNeverFallsBackToTenantless()
    {
        using var holder = new TenantContextAccessor();
        ITenantContextAccessor reader = holder;
        ITenantContextInitializer initializer = holder;

        Assert.Throws<InvalidOperationException>(() => reader.Current);
        Assert.Throws<ArgumentNullException>(() => initializer.Initialize(null!));
        Assert.Throws<InvalidOperationException>(() => reader.Current);

        TenantContext deliberate = TenantContext.Tenantless();
        initializer.Initialize(deliberate);
        Assert.Same(deliberate, reader.Current);
    }

    [Fact]
    public void SameOrDifferentContextCannotReplaceTheEstablishedValue()
    {
        using var holder = new TenantContextAccessor();
        ITenantContextAccessor reader = holder;
        ITenantContextInitializer initializer = holder;
        TenantContext first = TenantContext.ForTenant(new TenantId("alpha"));
        initializer.Initialize(first);

        Assert.Throws<InvalidOperationException>(() => initializer.Initialize(first));
        Assert.Throws<InvalidOperationException>(() =>
            initializer.Initialize(TenantContext.ForTenant(new TenantId("beta")))
        );
        Assert.Same(first, reader.Current);
    }

    [Fact]
    public async Task CompetingInitializersPublishExactlyOneCompleteContext()
    {
        using var holder = new TenantContextAccessor();
        ITenantContextAccessor reader = holder;
        ITenantContextInitializer initializer = holder;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TenantContext alpha = TenantContext.ForTenant(new TenantId("alpha"));
        TenantContext beta = TenantContext.ForTenant(new TenantId("beta"));

        Task<TenantContext?>[] attempts = [AttemptAsync(alpha), AttemptAsync(beta)];
        start.SetResult();
        TenantContext?[] results = await Task.WhenAll(attempts)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        TenantContext winner = Assert.Single(results.OfType<TenantContext>());
        Assert.Single(results, result => result is null);
        Assert.Same(winner, reader.Current);
        Assert.Equal(winner.Tenant, reader.Current.Tenant);

        async Task<TenantContext?> AttemptAsync(TenantContext context)
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
        using var holder = new TenantContextAccessor();
        ITenantContextAccessor reader = holder;
        TenantContext expected = TenantContext.ForTenant(new TenantId("alpha"));
        holder.Initialize(expected);

        Task[] reads = Enumerable
            .Range(0, 16)
            .Select(_ =>
                Task.Run(
                    () =>
                    {
                        TenantContext actual = reader.Current;
                        Assert.Same(expected, actual);
                        Assert.Equal("alpha", actual.RequireTenant().Value);
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
        var holder = new TenantContextAccessor();
        ITenantContextAccessor reader = holder;
        ITenantContextInitializer initializer = holder;
        TenantContext context = TenantContext.Tenantless();
        if (initialize)
        {
            initializer.Initialize(context);
        }

        holder.Dispose();
        holder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reader.Current);
        Assert.Throws<ObjectDisposedException>(() => initializer.Initialize(context));
        Assert.Null(context.Tenant);
    }
}
