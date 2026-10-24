using Gloaming.EventSourcing.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Gloaming.EventSourcing.Postgres.Tests.FeedConsumer;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class FeedTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly FeedLimits Limits = new(32, 1024 * 1024);

    [Fact]
    public async Task PopulatedBaselineAndMultiSourceTransactionsUseCompleteKeysAndStreamOrder()
    {
        string connection = await SetupAsync(install: false);
        await LedgerAsync(connection, 1, 10, Token);
        await InstallAsync(connection, Token);
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(Token);
        await using var transaction = await session.BeginTransactionAsync(Token);
        await LedgerAsync(session, transaction, 3, 7, Token);
        await LedgerAsync(session, transaction, 2, 5, Token);
        await using (
            var reviews = new NpgsqlCommand(
                """
                INSERT INTO app.reviews VALUES
                    ('Tenant', '00000000-0000-0000-0000-000000000091', 'same', 1, 'review.contributed', 1, '{}'),
                    ('tenant', '00000000-0000-0000-0000-000000000091', 'same', 1, 'review.contributed', 1, '{}')
                """,
                session,
                transaction
            )
        )
            await reviews.ExecuteNonQueryAsync(Token);

        await transaction.CommitAsync(Token);
        await using var services = Services(connection);
        var feed = Feed(services);
        var page = await feed.ReadAsync(FeedCursor.Start(Location.FeedId), Limits, Token);
        Assert.Equal(
            [1L, 2L, 3L],
            page.Events.Where(x => x.Source == "ledger").Select(x => x.Version)
        );
        Assert.Equal(
            ["Tenant", "tenant"],
            page.Events.Where(x => x.Source == "reviews").Select(x => x.StreamKey[0].GetString())
        );
        Assert.Equal([1L, 2L, 2L, 2L, 2L], page.Events.Select(x => x.Position));
        Assert.Equal(
            10,
            page.Events[0].Envelope.GetProperty("payload").GetProperty("amount").GetInt32()
        );
        Assert.False(page.HasMore);
        Assert.Empty(
            (await feed.ReadAsync(FeedCursor.Parse(page.Next.Value), Limits, Token)).Events
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldFirstWriterBlocksLaterPublicationUntilCommitOrRollback(bool rollback)
    {
        string connection = await SetupAsync();
        await using var first = new NpgsqlConnection(connection);
        await using var second = new NpgsqlConnection(connection);
        await first.OpenAsync(Token);
        await second.OpenAsync(Token);
        await using var firstTransaction = await first.BeginTransactionAsync(Token);
        await using var secondTransaction = await second.BeginTransactionAsync(Token);
        await LedgerAsync(first, firstTransaction, 1, 10, Token);
        var pending = LedgerAsync(second, secondTransaction, 2, 5, Token);
        await WaitBlockedAsync(connection, second.ProcessID, Token);
        await using var services = Services(connection);
        var feed = Feed(services);
        var before = await feed.ReadAsync(FeedCursor.Start(Location.FeedId), Limits, Token);
        Assert.Empty(before.Events);
        Assert.Equal(1, before.ObservedHead);
        if (rollback)
            await firstTransaction.RollbackAsync(Token);
        else
            await firstTransaction.CommitAsync(Token);

        await pending;
        await secondTransaction.CommitAsync(Token);
        var after = await feed.ReadAsync(before.Next, Limits, Token);
        Assert.Equal(rollback ? [2L] : [1L, 2L], after.Events.Select(x => x.Version));
        Assert.Equal(rollback ? [2L] : [2L, 3L], after.Events.Select(x => x.Position));
    }

    [Fact]
    public async Task OlderTransactionWithUnrelatedWriteCanAppendAfterNewerStreamCommit()
    {
        string connection = await SetupAsync();
        await SqlAsync(connection, "CREATE TABLE app.unrelated(id integer PRIMARY KEY)", Token);
        await using var older = new NpgsqlConnection(connection);
        await older.OpenAsync(Token);
        await using var transaction = await older.BeginTransactionAsync(Token);
        await using (
            var unrelated = new NpgsqlCommand(
                "INSERT INTO app.unrelated VALUES(1)",
                older,
                transaction
            )
        )
            await unrelated.ExecuteNonQueryAsync(Token);

        await LedgerAsync(connection, 1, 10, Token);
        await using (
            var observe = new NpgsqlCommand(
                "SELECT max(version) FROM app.ledger",
                older,
                transaction
            )
        )
            Assert.Equal(1L, await observe.ExecuteScalarAsync(Token));

        await LedgerAsync(older, transaction, 2, 5, Token);
        await transaction.CommitAsync(Token);
        await using var services = Services(connection);
        var page = await Feed(services).ReadAsync(FeedCursor.Start(Location.FeedId), Limits, Token);
        Assert.Equal([1L, 2L], page.Events.Select(x => x.Version));
        Assert.Equal([2L, 3L], page.Events.Select(x => x.Position));
    }

    [Fact]
    public async Task RollbackBeforeAndAfterFirstCapturePreservesAtomicHeadAndRows()
    {
        string connection = await SetupAsync();
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(Token);
        await using var transaction = await session.BeginTransactionAsync(Token);
        await transaction.SaveAsync("before", Token);
        await LedgerAsync(session, transaction, 1, 999, Token);
        await transaction.RollbackAsync("before", Token);
        await LedgerAsync(session, transaction, 1, 10, Token);
        await transaction.SaveAsync("after", Token);
        await LedgerAsync(session, transaction, 2, 999, Token);
        await transaction.RollbackAsync("after", Token);
        await LedgerAsync(session, transaction, 2, 5, Token);
        await transaction.CommitAsync(Token);
        await using var services = Services(connection);
        var page = await Feed(services).ReadAsync(FeedCursor.Start(Location.FeedId), Limits, Token);
        Assert.Equal(
            [10, 5],
            page.Events.Select(x =>
                x.Envelope.GetProperty("payload").GetProperty("amount").GetInt32()
            )
        );
        Assert.All(page.Events, x => Assert.Equal(2, x.Position));
    }

    [Fact]
    public async Task BoundedPagesResumeInsideOneTransactionAndRejectOversizedFirstEvent()
    {
        string connection = await SetupAsync();
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(Token);
        await using var transaction = await session.BeginTransactionAsync(Token);
        for (int i = 5; i >= 1; i--)
            await LedgerAsync(session, transaction, i, i, Token);

        await transaction.CommitAsync(Token);
        await using var services = Services(connection);
        var feed = Feed(services);
        var cursor = FeedCursor.Start(Location.FeedId);
        await Assert.ThrowsAsync<FeedEventTooLargeException>(() =>
            feed.ReadAsync(cursor, new(2, 1), Token)
        );
        var observed = new List<long>();
        FeedPage page;
        do
        {
            page = await feed.ReadAsync(FeedCursor.Parse(cursor.Value), new(2, 1024 * 1024), Token);
            Assert.InRange(page.Events.Count, 1, 2);
            observed.AddRange(page.Events.Select(x => x.Version));
            cursor = page.Next;
        } while (page.HasMore);

        Assert.Equal([1L, 2L, 3L, 4L, 5L], observed);
        Assert.Empty((await feed.ReadAsync(cursor, Limits, Token)).Events);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            feed.ReadAsync(FeedCursor.Start(Guid.NewGuid()), Limits, Token)
        );
    }

    [Fact]
    public async Task TwoIndependentReducersCommitRowsAndProgressAndResumeWithoutDuplicates()
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await LedgerAsync(connection, 2, 5, Token);
        await SqlAsync(
            connection,
            "INSERT INTO app.reviews VALUES('Tenant', '00000000-0000-0000-0000-000000000091', 'same', 1, 'review.contributed', 1, '{}')",
            Token
        );
        await using var services = Services(connection);
        var feed = Feed(services);
        Assert.Equal(
            ProjectionStatus.Applied,
            (await feed.ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
        Assert.Equal(
            ProjectionStatus.Applied,
            (await feed.ProjectNextAsync(Reviews(), Limits, Token)).Status
        );
        Assert.Equal(
            ProjectionStatus.NoWork,
            (await feed.ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
        Assert.Equal(
            ProjectionStatus.NoWork,
            (await feed.ProjectNextAsync(Reviews(), Limits, Token)).Status
        );
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<Views>();
        Assert.Equal(15, (await database.Set<LedgerView>().SingleAsync(Token)).Amount);
        Assert.Equal(1, (await database.Set<ReviewView>().SingleAsync(Token)).Amount);
        Assert.Equal(
            ProjectionStatus.Failed,
            (await feed.ProjectNextAsync(Ledger(definition: "changed"), Limits, Token)).Status
        );
    }

    [Theory]
    [InlineData("poison")]
    [InlineData("save")]
    [InlineData("commit")]
    [InlineData("constraint")]
    public async Task CallbackFailuresAndUnsupportedCommitDetectionPermitFreshRecovery(
        string failure
    )
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var services = Services(connection);
        var feed = Feed(services);
        var bad = new ProjectionGeneration<Views>(
            "ledger",
            "v1",
            "ledger-v1",
            async (database, generation, _, token) =>
            {
                database.Add(
                    new LedgerView
                    {
                        Generation = generation,
                        Id = StreamId,
                        Amount = 999,
                    }
                );
                if (failure == "poison")
                    throw new InvalidOperationException("unknown payload");

                if (failure == "save")
                    await database.SaveChangesAsync(token);
                else if (failure == "commit")
                    await database.Database.CurrentTransaction!.CommitAsync(token);
                else
                    database.Add(
                        new LedgerView
                        {
                            Generation = generation,
                            Id = Guid.Empty,
                            Amount = -1,
                        }
                    );
            }
        );
        if (failure == "constraint")
            await SqlAsync(connection, "ALTER TABLE app.ledger_view ADD CHECK(amount >= 0)", Token);

        var result = await feed.ProjectNextAsync(bad, Limits, Token);
        Assert.Equal(ProjectionStatus.Failed, result.Status);
        // Committing the native transaction is an explicitly unsupported callback escape. It can
        // persist earlier raw SQL, so this case claims detection only, never rollback of misuse.
        await using (var session = new NpgsqlConnection(connection))
        {
            await session.OpenAsync(Token);
            await using var query = new NpgsqlCommand(
                "SELECT count(*) FROM app.ledger_view",
                session
            );
            Assert.Equal(0L, await query.ExecuteScalarAsync(Token));
            await using var checkpoint = new NpgsqlCommand(
                "SELECT count(*) FROM feed.projection_checkpoints",
                session
            );
            Assert.Equal(failure == "commit" ? 1L : 0L, await checkpoint.ExecuteScalarAsync(Token));
        }

        Assert.Equal(
            ProjectionStatus.Applied,
            (await feed.ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
    }

    [Fact]
    public async Task ConcurrentWorkerIsBusyAndCancellationRollsBackItsOwnTurn()
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var services = Services(connection);
        var feed = Feed(services);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var holding = new ProjectionGeneration<Views>(
            "ledger",
            "v1",
            "ledger-v1",
            async (database, generation, _, token) =>
            {
                database.Add(
                    new LedgerView
                    {
                        Generation = generation,
                        Id = StreamId,
                        Amount = 999,
                    }
                );
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        );
        var first = feed.ProjectNextAsync(holding, Limits, cancellation.Token);
        await entered.Task.WaitAsync(Token);
        Assert.Equal(
            ProjectionStatus.Busy,
            (await feed.ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
        await cancellation.CancelAsync();
        Assert.Equal(ProjectionStatus.Cancelled, (await first).Status);
        Assert.Equal(
            ProjectionStatus.Applied,
            (await feed.ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
    }

    [Fact]
    public async Task NewGenerationCatchesLiveAppendsAndCutoverUsesExpectedPointerAndCaughtUpFence()
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var services = Services(connection);
        var feed = Feed(services);
        await feed.ProjectNextAsync(Ledger(), Limits, Token);
        Assert.Equal(
            ProjectionStatus.Activated,
            (await feed.ActivateAsync("ledger", "v1", null, Token)).Status
        );
        await LedgerAsync(connection, 2, 5, Token);
        Assert.Equal(
            ProjectionStatus.AlreadyActive,
            (await feed.ActivateAsync("ledger", "v1", null, Token)).Status
        );
        await feed.ProjectNextAsync(Ledger("v2"), new(1, 1024 * 1024), Token);
        Assert.Equal(
            ProjectionStatus.NotCaughtUp,
            (await feed.ActivateAsync("ledger", "v2", "v1", Token)).Status
        );
        await feed.ProjectNextAsync(Ledger("v2"), Limits, Token);
        await LedgerAsync(connection, 3, 7, Token);
        Assert.Equal(
            ProjectionStatus.NotCaughtUp,
            (await feed.ActivateAsync("ledger", "v2", "v1", Token)).Status
        );
        await feed.ProjectNextAsync(Ledger("v2"), Limits, Token);
        Assert.Equal(
            ProjectionStatus.Conflict,
            (await feed.ActivateAsync("ledger", "v2", null, Token)).Status
        );
        Assert.Equal(
            ProjectionStatus.Activated,
            (await feed.ActivateAsync("ledger", "v2", "v1", Token)).Status
        );
        Assert.Equal(
            ProjectionStatus.AlreadyActive,
            (await feed.ActivateAsync("ledger", "v2", "v1", Token)).Status
        );
        await feed.ProjectNextAsync(Ledger(), Limits, Token);
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(Token);
        await using var query = new NpgsqlCommand(
            "SELECT v.amount FROM app.ledger_view v JOIN feed.projection_active a ON a.name = 'ledger' AND a.generation = v.generation",
            session
        );
        Assert.Equal(22, await query.ExecuteScalarAsync(Token));
    }

    [Theory]
    [InlineData("UPDATE app.ledger SET schema = 2")]
    [InlineData("DELETE FROM app.ledger")]
    [InlineData("TRUNCATE app.ledger")]
    public async Task RetainedSourceMutationIsRefused(string sql)
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await Assert.ThrowsAsync<PostgresException>(() => SqlAsync(connection, sql, Token));
    }

    private async Task<string> SetupAsync(bool install = true)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await SchemaAsync(connection, Token);
        if (install)
            await InstallAsync(connection, Token);

        return connection;
    }

    internal static async Task WaitBlockedAsync(string connection, int pid, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(deadline.Token);
        while (true)
        {
            await using var query = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid = @pid AND wait_event_type = 'Lock')",
                observer
            );
            query.Parameters.AddWithValue("pid", pid);
            if ((bool)(await query.ExecuteScalarAsync(deadline.Token))!)
                return;

            await Task.Delay(20, deadline.Token);
        }
    }
}
