using System.Data.Common;
using Gloaming.EntityFrameworkCore;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Gloaming.EventSourcing.Postgres.Tests.RebuildConsumer;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class MaintenanceParticipationTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AppendCannotOmitAnAdvancedTimestampThroughOriginalValues(
        bool asynchronous,
        bool header
    )
    {
        var (connection, id) = await SeedAsync(postgres);
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var store = new LedgerStore(database, clock: new LaterClock());
            var ledger = (await store.GetForWritingAsync(id, cancellationToken: Token))!;
            ledger.Move(5);
            await store.AppendAsync(ledger, Token);
            if (header)
            {
                var entry = database.ChangeTracker.Entries<LedgerStream>().Single();
                entry.Property(row => row.UpdatedAt).OriginalValue = entry.Entity.UpdatedAt;
                entry.Property(row => row.UpdatedAt).IsModified = false;
                Assert.Equal(new LaterClock().GetUtcNow(), entry.Entity.UpdatedAt);
                Assert.False(entry.Property(row => row.UpdatedAt).IsModified);
            }
            else
            {
                var entry = database.ChangeTracker.Entries<BalanceRow>().Single();
                entry.Property(row => row.RecordedAt).OriginalValue = entry.Entity.RecordedAt;
                entry.Property(row => row.RecordedAt).IsModified = false;
                Assert.Equal(new LaterClock().GetUtcNow(), entry.Entity.RecordedAt);
                Assert.False(entry.Property(row => row.RecordedAt).IsModified);
            }

            if (asynchronous)
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    database.SaveChangesAsync(Token)
                );
            else
                Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        }

        await AssertCommittedAsync(connection, id);
    }

    private sealed class LaterClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredAppendTimestampReadsAreAsyncAndCancellable(bool cancelRead)
    {
        var (connection, id) = await SeedAsync(postgres);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var commands = new AsyncReads();
        var services = new ServiceCollection();
        services.AddEventStreamValidation<LedgerDatabase>();
        services.AddDbContext<LedgerDatabase>(
            (dependencies, options) =>
                options
                    .UseNpgsql(connection)
                    .AddInterceptors(commands)
                    .ConfigureGloamingCapabilities<LedgerDatabase>(dependencies)
        );
        services.AddScoped(dependencies => new LedgerDatabase(
            dependencies.GetRequiredService<DbContextOptions<LedgerDatabase>>(),
            useCoordinator: true
        ));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LedgerDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        // The existing timestamp is valid: an equal-time append needs no timestamp UPDATE.
        var store = new LedgerStore(database);
        var ledger = (await store.GetForWritingAsync(id, cancellationToken: Token))!;
        ledger.Move(5);
        await store.AppendAsync(ledger, Token);
        database.ChangeTracker.DetectChanges();
        commands.Observe = true;
        commands.CancelRead = cancelRead ? cancellation.Cancel : null;
        if (cancelRead)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                database.SaveChangesAsync(cancellation.Token)
            );
            await transaction.RollbackAsync(Token);
        }
        else
        {
            await database.SaveChangesAsync(cancellation.Token);
            await transaction.CommitAsync(Token);
        }

        Assert.True(commands.ReadCount > 0);
        await AssertCommittedAsync(connection, id, cancelRead ? 15 : 20, cancelRead ? 2 : 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingRejectsOriginalValueChangesThatOmitReconstructedState(bool asynchronous)
    {
        var (connection, id) = await BehindAndCorruptAsync();
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new LedgerRebuilder(database).RebuildAsync(id, Token);
        var state = database.ChangeTracker.Entries<BalanceRow>().Single();
        state.Property(row => row.State).OriginalValue = state.Entity.State;
        state.Property(row => row.State).IsModified = false;

        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
        else
            Assert.Throws<InvalidOperationException>(() => database.SaveChanges());

        await transaction.RollbackAsync(Token);
        await AssertUnrepairedAsync(connection, missing: false);
        await AssertFreshRepairAsync(connection, id);
    }

    [Fact]
    public async Task RegenerationRejectsOriginalValueChangesWithoutReportingCompletion()
    {
        var (connection, id) = await BehindAndCorruptAsync();
        var services = new ServiceCollection();
        services.AddDbContext<LedgerDatabase>(options => options.UseNpgsql(connection));
        services.AddScoped<IAggregateRebuilder<Ledger>, OmittedState>();
        await using var provider = services.BuildServiceProvider();
        var executor = new AggregateRegenerator<Ledger, LedgerDatabase>(
            provider.GetRequiredService<IServiceScopeFactory>()
        );

        var result = await executor.RegenerateAsync(id, new(), cancellationToken: Token);

        Assert.Equal(AggregateRegenerationStatus.Failed, result.Status);
        Assert.IsType<InvalidOperationException>(result.Error);
        await AssertUnrepairedAsync(connection, missing: false);
        await AssertFreshRepairAsync(connection, id);
    }

    private async Task<(string Connection, Guid Id)> BehindAndCorruptAsync()
    {
        var seeded = await SeedAsync(postgres);
        await ExecuteAsync(
            seeded.Connection,
            "UPDATE ledger.balances SET \"Version\" = 1, \"State\" = '[]'"
        );
        return seeded;
    }

    private static async Task AssertFreshRepairAsync(string connection, Guid id)
    {
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new LedgerRebuilder(database).RebuildAsync(id, Token);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await AssertCommittedAsync(connection, id);
    }

    private sealed class OmittedState(LedgerDatabase database) : IAggregateRebuilder<Ledger>
    {
        public async Task<AggregateRebuildResult?> RebuildAsync(
            Guid id,
            CancellationToken cancellationToken = default
        )
        {
            var result = await new LedgerRebuilder(database).RebuildAsync(id, cancellationToken);
            var state = database.ChangeTracker.Entries<BalanceRow>().Single();
            state.Property(row => row.State).OriginalValue = state.Entity.State;
            state.Property(row => row.State).IsModified = false;
            return result;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegenerationRejectsPrematureAcceptanceWithoutReportingCompletion(bool missing)
    {
        var (connection, id) = await DamagedAsync(missing);
        var services = new ServiceCollection();
        services.AddDbContext<LedgerDatabase>(options => options.UseNpgsql(connection));
        services.AddScoped<IAggregateRebuilder<Ledger>, PrematureAcceptance>();
        await using var provider = services.BuildServiceProvider();
        var executor = new AggregateRegenerator<Ledger, LedgerDatabase>(
            provider.GetRequiredService<IServiceScopeFactory>()
        );

        var result = await executor.RegenerateAsync(id, new(), cancellationToken: Token);

        Assert.Equal(AggregateRegenerationStatus.Failed, result.Status);
        Assert.IsType<InvalidOperationException>(result.Error);
        await AssertUnrepairedAsync(connection, missing);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SavingRejectsSuppressionOfEitherMaintenanceParticipant(
        bool missing,
        bool asynchronous,
        bool suppressHeader
    )
    {
        var (connection, id) = await DamagedAsync(missing);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new LedgerRebuilder(database).RebuildAsync(id, Token);
        object participant = suppressHeader
            ? database.ChangeTracker.Entries<LedgerStream>().Single().Entity
            : database.ChangeTracker.Entries<BalanceRow>().Single().Entity;
        database.Entry(participant).State = EntityState.Unchanged;

        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
        else
            Assert.Throws<InvalidOperationException>(() => database.SaveChanges());

        await transaction.RollbackAsync(Token);
        await AssertUnrepairedAsync(connection, missing);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SavingRejectsRepairUndoneByNativeSavepoint(bool missing, bool asynchronous)
    {
        var (connection, id) = await DamagedAsync(missing);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await transaction.CreateSavepointAsync("before_repair", Token);
        await new LedgerRebuilder(database).RebuildAsync(id, Token);
        await SaveAsync(database, asynchronous);
        await transaction.RollbackToSavepointAsync("before_repair", Token);

        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
        else
            Assert.Throws<InvalidOperationException>(() => database.SaveChanges());

        await transaction.RollbackAsync(Token);
        await AssertUnrepairedAsync(connection, missing);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StoredRepairAllowsLaterSavesAfterNativeAcceptance(
        bool asynchronous,
        bool acceptAllChangesOnSuccess
    )
    {
        var (connection, id) = await DamagedAsync(false);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new LedgerRebuilder(database).RebuildAsync(id, Token);
        if (asynchronous)
            await database.SaveChangesAsync(acceptAllChangesOnSuccess, Token);
        else
            database.SaveChanges(acceptAllChangesOnSuccess);

        database.ChangeTracker.AcceptAllChanges();
        await SaveAsync(database, asynchronous);
        await transaction.CommitAsync(Token);
        await AssertCommittedAsync(connection, id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HealthyUnchangedInlineStateDoesNotPreventHeaderStampAdvancing(
        bool asynchronous
    )
    {
        var (connection, id) = await SeedAsync(postgres);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new LedgerRebuilder(database).RebuildAsync(id, Token);
        var header = database.ChangeTracker.Entries<LedgerStream>().Single();
        var observedStamp = header.Property(row => row.ConcurrencyStamp).OriginalValue;
        // Its complete values already exist; only the new header stamp needs a write.
        database.ChangeTracker.Entries<BalanceRow>().Single().State = EntityState.Unchanged;

        await SaveAsync(database, asynchronous);
        await SaveAsync(database, asynchronous);
        await transaction.CommitAsync(Token);

        await using var verify = Open(connection);
        Assert.NotEqual(
            observedStamp,
            (await verify.Set<LedgerStream>().SingleAsync(Token)).ConcurrencyStamp
        );
        await AssertCommittedAsync(connection, id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredAsyncValidationUsesCancellableNativeReads(bool cancelRead)
    {
        var (connection, id) = await SeedAsync(postgres);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var commands = new AsyncReads();
        var services = new ServiceCollection();
        services.AddEventStreamValidation<LedgerDatabase>();
        services.AddDbContext<LedgerDatabase>(
            (dependencies, options) =>
                options
                    .UseNpgsql(connection)
                    .AddInterceptors(commands)
                    .ConfigureGloamingCapabilities<LedgerDatabase>(dependencies)
        );
        services.AddScoped(dependencies => new LedgerDatabase(
            dependencies.GetRequiredService<DbContextOptions<LedgerDatabase>>(),
            useCoordinator: true
        ));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LedgerDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new LedgerRebuilder(database).RebuildAsync(id, Token);
        database.ChangeTracker.Entries<BalanceRow>().Single().State = EntityState.Unchanged;
        commands.Observe = true;
        commands.CancelRead = cancelRead ? cancellation.Cancel : null;

        if (cancelRead)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                database.SaveChangesAsync(cancellation.Token)
            );
            await transaction.RollbackAsync(Token);
        }
        else
        {
            await database.SaveChangesAsync(cancellation.Token);
            await database.SaveChangesAsync(cancellation.Token);
            await transaction.CommitAsync(Token);
        }

        Assert.True(commands.ReadCount > 0);
        await AssertCommittedAsync(connection, id);
    }

    private async Task<(string Connection, Guid Id)> DamagedAsync(bool missing)
    {
        var seeded = await SeedAsync(postgres);
        await ExecuteAsync(
            seeded.Connection,
            missing ? "DELETE FROM ledger.balances" : "UPDATE ledger.balances SET \"State\" = '[]'"
        );
        return seeded;
    }

    private static async Task AssertUnrepairedAsync(string connection, bool missing)
    {
        await using var verify = Open(connection);
        var row = await verify.Set<BalanceRow>().SingleOrDefaultAsync(Token);
        if (missing)
            Assert.Null(row);
        else
            Assert.Equal("[]", Assert.IsType<BalanceRow>(row).State.GetRawText());
    }

    private static Task SaveAsync(LedgerDatabase database, bool asynchronous)
    {
        if (asynchronous)
            return database.SaveChangesAsync(Token);

        database.SaveChanges();
        return Task.CompletedTask;
    }

    private sealed class PrematureAcceptance(LedgerDatabase database) : IAggregateRebuilder<Ledger>
    {
        public async Task<AggregateRebuildResult?> RebuildAsync(
            Guid id,
            CancellationToken cancellationToken = default
        )
        {
            var result = await new LedgerRebuilder(database).RebuildAsync(id, cancellationToken);
            database.ChangeTracker.AcceptAllChanges();
            return result;
        }
    }

    private sealed class AsyncReads : DbCommandInterceptor
    {
        internal bool Observe { get; set; }
        internal Action? CancelRead { get; set; }
        internal int ReadCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        ) =>
            Observe
                ? throw new InvalidOperationException(
                    "Async validation must not issue synchronous reads."
                )
                : result;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Observe)
            {
                ReadCount++;
                Assert.True(cancellationToken.CanBeCanceled);
                CancelRead?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.FromResult(result);
        }
    }
}
