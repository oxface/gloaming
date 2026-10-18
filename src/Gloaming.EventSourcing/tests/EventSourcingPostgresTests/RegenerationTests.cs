using System.Collections.Concurrent;
using System.Data.Common;
using System.Transactions;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;
using static Gloaming.EventSourcing.Postgres.Tests.RebuildConsumer;
using IsolationLevel = System.Data.IsolationLevel;

namespace Gloaming.EventSourcing.Postgres.Tests;

// Independent direct-JSON adopter: no Wholesale, tenancy, codec, actor or host policy.
public sealed class RegenerationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeExecutorCompletesRepairWithOrderedProgressAndNoConsumerTransaction(
        bool missing
    )
    {
        var (connection, id) = await SeedAsync(postgres);
        await ExecuteAsync(
            connection,
            missing ? "DELETE FROM ledger.balances" : "UPDATE ledger.balances SET \"State\" = '[]'"
        );
        var native = new SaveObservation();
        await using var provider = Services(connection, native).BuildServiceProvider();
        var reports = new List<AggregateRegenerationProgress>();
        var result = await Executor(provider)
            .RegenerateAsync(
                id,
                new(),
                progress: new Observer(reports.Add),
                cancellationToken: Token
            );
        Assert.Equal(AggregateRegenerationStatus.Completed, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(2, result.CapturedHead!.Version);
        Assert.Null(result.Error);
        Assert.Equal(
            [AggregateRegenerationPhase.Started, AggregateRegenerationPhase.Replayed],
            reports.Select(item => item.Phase)
        );
        Assert.Single(native.Contexts);
        AssertDisposed(native.Contexts);
        await AssertCommittedAsync(connection, id);
        var repeated = await Executor(provider)
            .RegenerateAsync(id, new(), cancellationToken: Token);
        Assert.Equal(AggregateRegenerationStatus.Completed, repeated.Status);
        await AssertCommittedAsync(connection, id);
    }

    [Fact]
    public async Task ScopeFactoryInitializesBeforeNativeResolutionInEveryFreshAttempt()
    {
        var (connection, id) = await SeedAsync(postgres);
        int writes = 0;
        var native = new SaveObservation
        {
            BeforeSave = async () =>
            {
                if (writes++ == 0)
                    await MoveAsync(connection, id);
            },
        };
        var services = Services(connection, native);
        services.AddScoped<ScopeState>();
        services.AddDbContext<LedgerDatabase>(
            (scope, _) => Assert.True(scope.GetRequiredService<ScopeState>().Initialized)
        );
        await using var provider = services.BuildServiceProvider();
        var initializations = new List<ScopeState>();
        var result = await Executor(
                provider,
                scope =>
                {
                    var state = scope.GetRequiredService<ScopeState>();
                    Assert.False(state.Initialized);
                    state.Initialized = true;
                    initializations.Add(state);
                }
            )
            .RegenerateAsync(
                id,
                new() { MaxAttempts = 2 },
                progress: new Observer(item =>
                {
                    if (item.Phase == AggregateRegenerationPhase.Retrying)
                        AssertDisposed(native.Contexts);
                }),
                cancellationToken: Token
            );
        Assert.Equal(AggregateRegenerationStatus.Completed, result.Status);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, initializations.Distinct().Count());
        Assert.Equal(2, native.Contexts.Distinct().Count());
        AssertDisposed(native.Contexts);
        await AssertCommittedAsync(connection, id, 16, 3);
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("started")]
    [InlineData("replayed")]
    [InlineData("retrying")]
    public async Task NativeConcurrencyFromScopeFactoryOrReporterDoesNotBecomeAnAutomaticRetry(
        string phase
    )
    {
        var (connection, id) = await SeedAsync(postgres);
        var error = new DbUpdateConcurrencyException("Unrelated observer or initializer failure.");
        var native = new SaveObservation
        {
            BeforeSave = phase == "retrying" ? () => MoveAsync(connection, id) : null,
        };
        await using var provider = Services(connection, native).BuildServiceProvider();
        var result = await Executor(
                provider,
                _ =>
                {
                    if (phase == "initialize")
                        throw error;
                }
            )
            .RegenerateAsync(
                id,
                new() { MaxAttempts = 3 },
                progress: new Observer(item =>
                {
                    if (
                        (phase, item.Phase)
                        is
                            ("started", AggregateRegenerationPhase.Started)
                            or
                            ("replayed", AggregateRegenerationPhase.Replayed)
                            or
                            ("retrying", AggregateRegenerationPhase.Retrying)
                    )
                        throw error;
                }),
                cancellationToken: Token
            );
        Assert.Equal(AggregateRegenerationStatus.Failed, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Same(error, result.Error);
        AssertDisposed(native.Contexts);
        await AssertCommittedAsync(
            connection,
            id,
            phase == "retrying" ? 16 : 15,
            phase == "retrying" ? 3 : 2
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealConcurrencyEntriesFromAnotherContextAreNotMaintenanceConflicts(
        bool duringSave
    )
    {
        var (connection, id) = await SeedAsync(postgres);
        async Task UnrelatedConflict()
        {
            await using var other = Open(connection);
            await using var transaction = await other.Database.BeginTransactionAsync(Token);
            var store = new LedgerStore(other);
            var ledger = (await store.GetForWritingAsync(id, cancellationToken: Token))!;
            ledger.Move(1);
            await store.AppendAsync(ledger, Token);
            await ExecuteAsync(
                connection,
                "UPDATE ledger.event_streams SET concurrency_stamp = gen_random_uuid()"
            );
            await other.SaveChangesAsync(Token);
        }

        var services = Services(
            connection,
            new SaveObservation { BeforeSave = duringSave ? UnrelatedConflict : null }
        );
        if (!duringSave)
            services.AddScoped<IAggregateRebuilder<Ledger>>(_ => new DelegateRebuilder(
                async (_, _) =>
                {
                    await UnrelatedConflict();
                    throw new InvalidOperationException(
                        "The actual SQL conflict must throw first."
                    );
                }
            ));

        await using var provider = services.BuildServiceProvider();
        var result = await Executor(provider)
            .RegenerateAsync(id, new() { MaxAttempts = 3 }, cancellationToken: Token);
        Assert.Equal(AggregateRegenerationStatus.Failed, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.NotEmpty(Assert.IsType<DbUpdateConcurrencyException>(result.Error).Entries);
        await AssertCommittedAsync(connection, id);
    }

    [Fact]
    public async Task PairingARebuilderWithAnotherNativeContextFailsBeforeSave()
    {
        var (connection, id) = await SeedAsync(postgres);
        var native = new SaveObservation();
        var services = Services(connection, native);
        services.AddScoped<IAggregateRebuilder<Ledger>>(_ => new DelegateRebuilder(
            async (target, token) =>
            {
                await using var other = Open(connection);
                await using var transaction = await other.Database.BeginTransactionAsync(token);
                return await new LedgerRebuilder(other).RebuildAsync(target, token);
            }
        ));
        await using var provider = services.BuildServiceProvider();
        var result = await Executor(provider).RegenerateAsync(id, new(), cancellationToken: Token);
        Assert.Equal(AggregateRegenerationStatus.Failed, result.Status);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Empty(native.Contexts);
        await AssertCommittedAsync(connection, id);
    }

    [Fact]
    public async Task CancellationBeforeScopeCreationHasZeroAttempts()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var result = await Executor(
                provider,
                _ => throw new InvalidOperationException("Must not run.")
            )
            .RegenerateAsync(Guid.NewGuid(), new(), cancellationToken: cancellation.Token);
        Assert.Equal(AggregateRegenerationStatus.Cancelled, result.Status);
        Assert.Equal(0, result.Attempts);
        Assert.Null(result.CapturedHead);
    }

    [Fact]
    public async Task CancellationDuringRetryDelayStartsNoNewScope()
    {
        var (connection, id) = await SeedAsync(postgres);
        var native = new SaveObservation { BeforeSave = () => MoveAsync(connection, id) };
        await using var provider = Services(connection, native).BuildServiceProvider();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var retrying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = Executor(provider)
            .RegenerateAsync(
                id,
                new() { MaxAttempts = 3, RetryDelay = TimeSpan.FromMinutes(1) },
                progress: new Observer(item =>
                {
                    if (item.Phase == AggregateRegenerationPhase.Retrying)
                        retrying.SetResult();
                }),
                cancellationToken: cancellation.Token
            );
        await retrying.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await cancellation.CancelAsync();
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Equal(AggregateRegenerationStatus.Cancelled, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Single(native.Contexts);
        AssertDisposed(native.Contexts);
        await AssertCommittedAsync(connection, id, 16, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupErrorPreservesKnownCommitOutcomeAndDoesNotRetry(bool commit)
    {
        var (connection, id) = await SeedAsync(postgres);
        await ExecuteAsync(connection, "UPDATE ledger.balances SET \"State\" = '{\"Amount\":99}'");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var native = new SaveObservation { AfterSave = commit ? null : cancellation.CancelAsync };
        var services = Services(connection, native);
        services.AddScoped<CleanupFailure>();
        await using var provider = services.BuildServiceProvider();
        var result = await Executor(
                provider,
                scope => _ = scope.GetRequiredService<CleanupFailure>()
            )
            .RegenerateAsync(id, new() { MaxAttempts = 3 }, cancellationToken: cancellation.Token);
        Assert.Equal(
            commit ? AggregateRegenerationStatus.Completed : AggregateRegenerationStatus.Cancelled,
            result.Status
        );
        Assert.Equal(1, result.Attempts);
        Assert.NotNull(result.Error);
        if (commit)
            Assert.IsType<IOException>(result.Error);
        else
            Assert.Contains(
                Assert.IsType<AggregateException>(result.Error).InnerExceptions,
                item => item is IOException
            );

        AssertDisposed(native.Contexts);
        await AssertCommittedAsync(connection, id, commit ? 15 : 99);
    }

    [Fact]
    public async Task AmbientTransactionIsRejectedBeforeConsumerInitialization()
    {
        var (connection, id) = await SeedAsync(postgres);
        await using var provider = Services(connection).BuildServiceProvider();
        using var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var result = await Executor(provider, _ => throw new IOException("Must not run."))
            .RegenerateAsync(id, new(), cancellationToken: Token);
        Assert.Equal(AggregateRegenerationStatus.Failed, result.Status);
        Assert.IsType<InvalidOperationException>(result.Error);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 4294967295)]
    public async Task InvalidPolicyIsRejectedBeforeScopeCreation(
        int attempts,
        long delayMilliseconds
    )
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Executor(provider)
                .RegenerateAsync(
                    Guid.NewGuid(),
                    new()
                    {
                        MaxAttempts = attempts,
                        RetryDelay = TimeSpan.FromMilliseconds(delayMilliseconds),
                    },
                    cancellationToken: Token
                )
        );
    }

    [Fact]
    public async Task NullAndEmptyArgumentsAreRejectedBeforeScopeCreation()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AggregateRegenerator<Ledger, LedgerDatabase>(null!)
        );
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Executor(provider).RegenerateAsync(Guid.Empty, new(), cancellationToken: Token)
        );
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Executor(provider).RegenerateAsync(Guid.NewGuid(), null!, cancellationToken: Token)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedHeaderMustCoverObservedAheadStateBeforeRetryIsAllowed(
        bool writerAdvanced
    )
    {
        var (connection, id) = await SeedAsync(postgres);
        var reads = new StateReadBarrier
        {
            BeforeRead = async () =>
            {
                if (writerAdvanced)
                    await MoveAsync(connection, id);
                else
                    await ExecuteAsync(
                        connection,
                        "UPDATE ledger.event_streams SET concurrency_stamp = gen_random_uuid()"
                    );

                // Neither the advanced head (3) nor an unchanged head (2) covers state version 4.
                await ExecuteAsync(connection, "UPDATE ledger.balances SET \"Version\" = 4");
            },
        };
        await using var provider = Services(connection, reads).BuildServiceProvider();
        var reports = new List<AggregateRegenerationProgress>();
        var result = await Executor(provider)
            .RegenerateAsync(
                id,
                new() { MaxAttempts = 3 },
                progress: new Observer(reports.Add),
                cancellationToken: Token
            );
        Assert.Equal(AggregateRegenerationStatus.Failed, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.IsType<InvalidDataException>(result.Error);
        Assert.DoesNotContain(reports, item => item.Phase == AggregateRegenerationPhase.Retrying);
    }

    [Fact]
    public async Task FailedScopeInitializationDisposesBeforeReturningFailureWithoutStartedProgress()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopeProbe>();
        await using var provider = services.BuildServiceProvider();
        ScopeProbe? probe = null;
        var error = new InvalidOperationException("Injected scope establishment failure.");
        var reports = new List<AggregateRegenerationProgress>();
        var result = await Executor(
                provider,
                scope =>
                {
                    probe = scope.GetRequiredService<ScopeProbe>();
                    throw error;
                }
            )
            .RegenerateAsync(
                Guid.NewGuid(),
                new() { MaxAttempts = 3 },
                new Observer(reports.Add),
                Token
            );
        Assert.Equal(AggregateRegenerationStatus.Failed, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Same(error, result.Error);
        Assert.Empty(reports);
        Assert.True(Assert.IsType<ScopeProbe>(probe).Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StandardProgressDeliveryCanFollowCommitAndCannotChangeReturnedOutcome(
        bool deliveryFails
    )
    {
        var (connection, id) = await SeedAsync(postgres);
        await using var provider = Services(connection).BuildServiceProvider();
        var dispatch = new DeferredProgressContext();
        var received = new List<AggregateRegenerationProgress>();
        var error = new IOException("Injected asynchronous progress-handler failure.");
        IProgress<AggregateRegenerationProgress> progress;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(dispatch);
            progress = new Progress<AggregateRegenerationProgress>(item =>
            {
                if (deliveryFails)
                    throw error;

                received.Add(item);
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var result = await Executor(provider).RegenerateAsync(id, new(), progress, Token);
        Assert.Equal(AggregateRegenerationStatus.Completed, result.Status);
        Assert.Null(result.Error);
        Assert.Empty(received);
        Assert.Equal(2, dispatch.PendingCount);
        await AssertCommittedAsync(connection, id);
        if (deliveryFails)
            Assert.Same(error, Assert.Throws<IOException>(dispatch.Deliver));
        else
        {
            dispatch.Deliver();
            Assert.Equal(
                [AggregateRegenerationPhase.Started, AggregateRegenerationPhase.Replayed],
                received.Select(item => item.Phase)
            );
        }

        Assert.Equal(AggregateRegenerationStatus.Completed, result.Status);
        Assert.Null(result.Error);
    }

    private static ServiceCollection Services(string connection, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LedgerDatabase>(options =>
            options.UseNpgsql(connection).AddInterceptors(interceptors)
        );
        services.AddAggregateRebuilder<Ledger, LedgerRebuilder>();
        return services;
    }

    private static AggregateRegenerator<Ledger, LedgerDatabase> Executor(
        ServiceProvider provider,
        Action<IServiceProvider>? initialize = null
    )
    {
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        return new(initialize is null ? scopes : new InitializedScopes(scopes, initialize));
    }

    private sealed class InitializedScopes(
        IServiceScopeFactory scopes,
        Action<IServiceProvider> initialize
    ) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            var scope = scopes.CreateScope();
            try
            {
                initialize(scope.ServiceProvider);
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }
    }

    private sealed class Observer(Action<AggregateRegenerationProgress> report)
        : IProgress<AggregateRegenerationProgress>
    {
        public void Report(AggregateRegenerationProgress value) => report(value);
    }

    private sealed class DeferredProgressContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> pending =
            new();
        internal int PendingCount => pending.Count;

        public override void Post(SendOrPostCallback callback, object? state) =>
            pending.Enqueue((callback, state));

        internal void Deliver()
        {
            while (pending.TryDequeue(out var item))
                item.Callback(item.State);
        }
    }

    private sealed class ScopeProbe : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private static async Task MoveAsync(string connection, Guid id)
    {
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var store = new LedgerStore(database);
        var ledger = (await store.GetForWritingAsync(id, cancellationToken: Token))!;
        ledger.Move(1);
        await store.AppendAsync(ledger, Token);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static void AssertDisposed(IEnumerable<DbContext> contexts)
    {
        foreach (var context in contexts)
            Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker.Entries().ToArray());
    }

    private sealed class ScopeState
    {
        internal bool Initialized { get; set; }
    }

    private sealed class CleanupFailure : IAsyncDisposable
    {
        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new IOException("Injected scope cleanup failure."));
    }

    private sealed class DelegateRebuilder(
        Func<Guid, CancellationToken, Task<AggregateRebuildResult?>> action
    ) : IAggregateRebuilder<Ledger>
    {
        public Task<AggregateRebuildResult?> RebuildAsync(
            Guid id,
            CancellationToken cancellationToken = default
        ) => action(id, cancellationToken);
    }

    private sealed class SaveObservation : SaveChangesInterceptor
    {
        internal Func<Task>? BeforeSave { get; init; }
        internal Func<Task>? AfterSave { get; init; }
        internal List<DbContext> Contexts { get; } = [];

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Contexts.Add(eventData.Context!);
            Assert.Equal(
                IsolationLevel.ReadCommitted,
                eventData.Context!.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel
            );
            if (BeforeSave is not null)
                await BeforeSave();

            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (AfterSave is not null)
                await AfterSave();

            return result;
        }
    }

    private sealed class StateReadBarrier : DbCommandInterceptor
    {
        internal Func<Task>? BeforeRead { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command.CommandText.Contains("FROM ledger.balances AS", StringComparison.Ordinal)
                && BeforeRead is { } action
            )
            {
                BeforeRead = null;
                await action();
            }

            return result;
        }
    }
}
