using System.Data;
using System.Data.Common;
using Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.CommandReceipts.Tests;

public sealed class ReceiptTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CommandReceiptKey Key(
        string scope = "tenant-a",
        string operation = "publish",
        Guid? id = null
    ) => new(scope, operation, id ?? Guid.NewGuid());

    [Fact]
    public async Task EqualAttemptsReplayOpaqueResultsAndChangedInputConflictsWithoutRepeatingEffects()
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        var key = Key();
        Guid publication = Guid.NewGuid();
        string result = await Consumer.PublishAsync(
            services,
            key,
            publication,
            "canonical-v1",
            Token
        );
        Assert.Equal("published:" + publication.ToString("D"), result);
        Assert.Equal(
            result,
            await Consumer.PublishAsync(services, key, publication, "canonical-v1", Token)
        );
        await Assert.ThrowsAsync<CommandReceiptConflictException>(() =>
            Consumer.PublishAsync(services, key, publication, "changed", Token)
        );
        await AssertCountsAsync(connection, 1);
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("sync-false")]
    [InlineData("async")]
    [InlineData("async-false")]
    public async Task AllNativeSaveFormsCommitTheReceiptAndBusinessIntentTogether(string save)
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PublishDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var attempt = await Store(scope).AcquireAsync(Key(), "input", Token);
        Stage(database, attempt);
        if (save == "sync")
            database.SaveChanges();
        else if (save == "sync-false")
            database.SaveChanges(false);
        else if (save == "async")
            await database.SaveChangesAsync(Token);
        else
            await database.SaveChangesAsync(false, Token);

        if (save.EndsWith("false", StringComparison.Ordinal))
            database.ChangeTracker.AcceptAllChanges();

        Assert.Equal(0, await database.SaveChangesAsync(Token));
        await AssertCountsAsync(connection, 0);
        await transaction.CommitAsync(Token);
        await AssertCountsAsync(connection, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RollingBackBeforeOrAfterSaveLeavesTheIdentityUnclaimed(bool save)
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        var key = Key();
        await using (var scope = services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PublishDatabase>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Stage(database, await Store(scope).AcquireAsync(key, "input", Token));
            if (save)
                await database.SaveChangesAsync(Token);

            await transaction.RollbackAsync(Token);
        }

        await AssertCountsAsync(connection, 0);
        await Consumer.PublishAsync(services, key, Guid.NewGuid(), "input", Token);
        await AssertCountsAsync(connection, 1);
    }

    [Fact]
    public async Task WaitingEqualWriterSeesTheCommittedResultFromAnotherContext()
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        var key = Key();
        Guid id = Guid.NewGuid();
        await using var first = services.CreateAsyncScope();
        var database = first.ServiceProvider.GetRequiredService<PublishDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var attempt = await Store(first).AcquireAsync(key, "input", Token);
        database.Add(new Publication { Id = id, Version = 1 });
        database.Add(new DispatchIntent { Id = Guid.NewGuid(), PublicationId = id });
        attempt.Retain(new(17, "consumer-owned-text"));
        var competing = Consumer.PublishAsync(services, key, id, "input", Token);
        await WaitForWaiterAsync(connection, Token);
        Assert.False(competing.IsCompleted);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal("consumer-owned-text", await competing);
        await AssertCountsAsync(connection, 1);
    }

    [Fact]
    public async Task CancellationFaultsOnlyTheWaitingScopeAndOwnerCanStillCommit()
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        var key = Key();
        await using var owner = services.CreateAsyncScope();
        var database = owner.ServiceProvider.GetRequiredService<PublishDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var attempt = await Store(owner).AcquireAsync(key, "input", Token);
        await using var waiter = services.CreateAsyncScope();
        var waitingDatabase = waiter.ServiceProvider.GetRequiredService<PublishDatabase>();
        await using var waitingTransaction = await waitingDatabase.Database.BeginTransactionAsync(
            Token
        );
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var waiting = Store(waiter).AcquireAsync(key, "input", cancel.Token);
        await WaitForWaiterAsync(connection, Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            waitingDatabase.SaveChangesAsync(Token)
        );
        Stage(database, attempt);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await AssertCountsAsync(connection, 1);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("detached")]
    [InlineData("deleted")]
    [InlineData("transaction")]
    public async Task ChangedOrSeparatedReceiptProposalCannotBeSavedWithBusinessChanges(
        string change
    )
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PublishDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Stage(database, await Store(scope).AcquireAsync(Key(), "input", Token));
        var row = Assert.Single(database.ChangeTracker.Entries<CommandReceiptRecord>());
        if (change == "payload")
            row.Property(value => value.Result).CurrentValue = "replaced";
        else if (change == "detached")
            row.State = EntityState.Detached;
        else if (change == "deleted")
            database.Remove(row.Entity);
        else
            await transaction.CommitAsync(Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        await AssertCountsAsync(connection, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackedCommittedReceiptsCannotBeUpdatedOrDeleted(bool delete)
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        await Consumer.PublishAsync(services, Key(), Guid.NewGuid(), "input", Token);
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PublishDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var row = await database.Set<CommandReceiptRecord>().SingleAsync(Token);
        if (delete)
            database.Remove(row);
        else
            database.Entry(row).Property(value => value.Result).CurrentValue = "replacement";

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        await AssertCountsAsync(connection, 1);
    }

    [Fact]
    public async Task RetentionHasNoAutomaticExpiryAndExplicitOwnerRemovalEndsProtection()
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        var key = Key();
        Guid id = Guid.NewGuid();
        await Consumer.PublishAsync(services, key, id, "input", Token);
        Assert.StartsWith(
            "published:",
            await Consumer.PublishAsync(services, key, id, "input", Token),
            StringComparison.Ordinal
        );
        // Deliberate native owner action, outside the append-only runtime contract.
        await SqlAsync(connection, "DELETE FROM publishing.results", Token);
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PublishDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var newLifetime = await Store(scope).AcquireAsync(key, "input", Token);
        Assert.Null(newLifetime.Result);
        var publication = await database.Set<Publication>().SingleAsync(Token);
        publication.Version++;
        database.Add(new DispatchIntent { Id = Guid.NewGuid(), PublicationId = id });
        newLifetime.Retain(new(1, "published-again"));
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(2, await CountAsync(connection, "publishing.dispatch_intents", Token));
        Assert.Equal(1, await CountAsync(connection, "publishing.results", Token));
    }

    [Fact]
    public async Task ScopeAndOperationNamespacesAreOrdinalAndIndependent()
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        Guid id = Guid.NewGuid();
        foreach (
            var key in new[]
            {
                Key("Tenant", "publish", id),
                Key("tenant", "publish", id),
                Key("Tenant", "Publish", id),
            }
        )
            await Consumer.PublishAsync(services, key, Guid.NewGuid(), "input", Token);

        await AssertCountsAsync(connection, 3);
    }

    [Fact]
    public async Task AmbiguousCommitRecoversACompletedReceiptInAFreshContext()
    {
        string connection = await SetupAsync();
        var key = Key();
        Guid id = Guid.NewGuid();
        await using (var uncertain = Consumer.Services(connection, new LoseCommitResponse()))
            await Assert.ThrowsAsync<IOException>(() =>
                Consumer.PublishAsync(uncertain, key, id, "input", Token)
            );

        await using var services = Consumer.Services(connection);
        Assert.Equal(
            "published:" + id.ToString("D"),
            await Consumer.PublishAsync(services, key, id, "input", Token)
        );
        await AssertCountsAsync(connection, 1);
    }

    [Fact]
    public async Task AcquisitionAndRetentionAreBoundToOneFreshContextAndActiveReadCommittedTransaction()
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        await using (var missing = services.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Store(missing).AcquireAsync(Key(), "input", Token)
            );

        await using (var oldSnapshot = services.CreateAsyncScope())
        {
            var database = oldSnapshot.ServiceProvider.GetRequiredService<PublishDatabase>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                Token
            );
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Store(oldSnapshot).AcquireAsync(Key(), "input", Token)
            );
        }

        await using var scope = services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<PublishDatabase>();
        await using var owned = await current.Database.BeginTransactionAsync(Token);
        var attempt = await Store(scope).AcquireAsync(Key(), "input", Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgresCommandReceipts<PublishDatabase>(current).AcquireAsync(
                Key(),
                "input",
                Token
            )
        );
        Assert.Throws<InvalidOperationException>(() => attempt.Retain(new(1, "result")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => current.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task RetainingTwiceOrAfterCommitRejectsSavingTheFailedOperation()
    {
        string connection = await SetupAsync();
        await using var services = Consumer.Services(connection);
        foreach (bool completed in new[] { false, true })
        {
            await using var scope = services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<PublishDatabase>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var attempt = await Store(scope).AcquireAsync(Key(), "input", Token);
            if (completed)
                await transaction.CommitAsync(Token);
            else
                attempt.Retain(new(1, "first"));

            Assert.Throws<InvalidOperationException>(() => attempt.Retain(new(1, "second")));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
        }

        await AssertCountsAsync(connection, 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ResultReadersMustHavePositiveFormatIdentities(int version) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CommandReceiptContent(version, "result")
        );

    [Fact]
    public void KeysAndPayloadsRejectUnsupportedBoundaryValues()
    {
        Assert.Throws<ArgumentException>(() =>
            new CommandReceiptKey("", "publish", Guid.NewGuid())
        );
        Assert.Throws<ArgumentException>(() => new CommandReceiptKey("scope", "", Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() =>
            new CommandReceiptKey(new string('a', 257), "publish", Guid.NewGuid())
        );
        Assert.Throws<ArgumentException>(() =>
            new CommandReceiptKey("scope", new string('a', 129), Guid.NewGuid())
        );
        Assert.Throws<ArgumentException>(() =>
            new CommandReceiptKey("scope\0", "publish", Guid.NewGuid())
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CommandReceiptKey("scope", "publish", Guid.Empty)
        );
        Assert.Throws<ArgumentNullException>(() => new CommandReceiptContent(1, null!));
        Assert.Throws<ArgumentException>(() => new CommandReceiptContent(1, "result\0"));
        Assert.Throws<ArgumentNullException>(() =>
            new PostgresCommandReceipts<PublishDatabase>(null!)
        );
    }

    [Fact]
    public void PhysicalNamesCannotBeTruncatedIntoTheSameTableUnderDifferentLockIdentities()
    {
        Assert.Throws<ArgumentException>(() =>
            new ModelBuilder().ConfigureCommandReceipts(new string('a', 64))
        );
        Assert.Throws<ArgumentException>(() =>
            new ModelBuilder().ConfigureCommandReceipts("schema", new string('b', 64))
        );
        Assert.Throws<ArgumentException>(() =>
            new ModelBuilder().ConfigureCommandReceipts(new string('é', 32))
        );
    }

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var services = Consumer.Services(connection);
        await using var scope = services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<PublishDatabase>()
            .Database.EnsureCreatedAsync(Token);
        return connection;
    }

    private static PostgresCommandReceipts<PublishDatabase> Store(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<PostgresCommandReceipts<PublishDatabase>>();

    private static void Stage(PublishDatabase database, CommandReceiptAttempt attempt)
    {
        Guid id = Guid.NewGuid();
        database.Add(new Publication { Id = id, Version = 1 });
        database.Add(new DispatchIntent { Id = Guid.NewGuid(), PublicationId = id });
        attempt.Retain(new(1, "opaque-result"));
    }

    private static async Task AssertCountsAsync(string connection, int expected)
    {
        foreach (
            string table in new[]
            {
                "publishing.results",
                "publishing.publications",
                "publishing.dispatch_intents",
            }
        )
            Assert.Equal(expected, await CountAsync(connection, table, Token));
    }

    internal static async Task<long> CountAsync(
        string connectionString,
        string table,
        CancellationToken token
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM " + table, connection);
        return (long)(await command.ExecuteScalarAsync(token))!;
    }

    internal static async Task SqlAsync(
        string connectionString,
        string sql,
        CancellationToken token
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task WaitForWaiterAsync(string connectionString, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(deadline.Token);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())",
            connection
        );
        while ((long)(await command.ExecuteScalarAsync(deadline.Token))! == 0)
            await Task.Delay(25, deadline.Token);
    }

    private sealed class LoseCommitResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) => throw new IOException("Commit succeeded; response was lost.");
    }
}
