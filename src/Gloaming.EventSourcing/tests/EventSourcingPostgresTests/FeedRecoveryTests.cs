using System.Data.Common;
using System.Globalization;
using System.Transactions;
using Gloaming.EventSourcing.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Gloaming.EventSourcing.Postgres.Tests.FeedConsumer;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class FeedRecoveryTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly FeedLimits Limits = new(32, 1024 * 1024);

    [Theory]
    [InlineData("staged", false)]
    [InlineData("saved", false)]
    [InlineData("committed", true)]
    public async Task ProcessDeathRecoversProjectionRowsAndRetainedProgressTogether(
        string phase,
        bool committed
    )
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var child = new ChildHost(
            typeof(FeedProof.Application).Assembly,
            Environment(connection),
            phase
        );
        await child.WaitForOutputAsync(phase.ToUpperInvariant(), Token);
        await child.KillAsync(Token);
        await using var services = Services(connection);
        var result = await Feed(services).ProjectNextAsync(Ledger(), Limits, Token);
        Assert.Equal(committed ? ProjectionStatus.NoWork : ProjectionStatus.Applied, result.Status);
        await using var scope = services.CreateAsyncScope();
        Assert.Equal(
            10,
            (
                await scope
                    .ServiceProvider.GetRequiredService<Views>()
                    .Set<LedgerView>()
                    .SingleAsync(Token)
            ).Amount
        );
    }

    [Theory]
    [InlineData("captured", false)]
    [InlineData("committed", true)]
    public async Task OldNativeWriterDeathCannotPublishAPartialOrLateSkippedCapture(
        string phase,
        bool committed
    )
    {
        string connection = await SetupAsync();
        await using var child = new ChildHost(
            typeof(FeedEarlier.Application).Assembly,
            Environment(connection),
            phase,
            "1"
        );
        await child.WaitForOutputAsync(phase.ToUpperInvariant(), Token);
        await using var services = Services(connection);
        var feed = Feed(services);
        var before = await feed.ReadAsync(FeedCursor.Start(Location.FeedId), Limits, Token);
        Assert.Equal(committed ? 1 : 0, before.Events.Count);
        await child.KillAsync(Token);
        await LedgerAsync(connection, committed ? 2 : 1, 5, Token);
        var after = await feed.ReadAsync(before.Next, Limits, Token);
        Assert.Equal(committed ? 2 : 1, Assert.Single(after.Events).Version);
        Assert.Equal(committed ? 3 : 2, after.ObservedHead);
    }

    [Fact]
    public async Task PopulatedInstallationWaitsForOldNativeWriterAndThenCapturesItsFutureWrites()
    {
        string connection = await SetupAsync(install: false);
        await using var gate = new NpgsqlConnection(connection);
        await gate.OpenAsync(Token);
        await using var gateTransaction = await gate.BeginTransactionAsync(Token);
        await using (
            var acquire = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(918273645::bigint)",
                gate,
                gateTransaction
            )
        )
            await acquire.ExecuteNonQueryAsync(Token);

        await using var old = new ChildHost(
            typeof(FeedEarlier.Application).Assembly,
            Environment(connection),
            "gate",
            "1"
        );
        await old.WaitForOutputAsync("CAPTURED", Token);
        await using var migration = new NpgsqlConnection(connection);
        await migration.OpenAsync(Token);
        await using var transaction = await migration.BeginTransactionAsync(Token);
        await using var installer = new NpgsqlCommand(
            EventFeedInstallation.Create(Location, Sources),
            migration,
            transaction
        );
        var installing = installer.ExecuteNonQueryAsync(Token);
        await FeedTests.WaitBlockedAsync(connection, migration.ProcessID, Token);
        await gateTransaction.CommitAsync(Token);
        Assert.Equal(0, await old.WaitForExitAsync(Token));
        await installing;
        await transaction.CommitAsync(Token);
        // The exact same separately compiled writer has no Gloaming reference or feed awareness.
        await using var after = new ChildHost(
            typeof(FeedEarlier.Application).Assembly,
            Environment(connection),
            "write",
            "2"
        );
        Assert.Equal(0, await after.WaitForExitAsync(Token));
        await using var services = Services(connection);
        var page = await Feed(services).ReadAsync(FeedCursor.Start(Location.FeedId), Limits, Token);
        Assert.Equal([1L, 2L], page.Events.Select(x => x.Position));
        Assert.Equal([1L, 2L], page.Events.Select(x => x.Version));
    }

    [Fact]
    public async Task CancelActuallyBlockedOldWriterThenFreshAppendHasNoPhantomBatch()
    {
        string connection = await SetupAsync();
        await using var holder = new NpgsqlConnection(connection);
        await holder.OpenAsync(Token);
        await using var transaction = await holder.BeginTransactionAsync(Token);
        await LedgerAsync(holder, transaction, 1, 10, Token);
        await using var child = new ChildHost(
            typeof(FeedEarlier.Application).Assembly,
            Environment(connection),
            "write",
            "2"
        );
        string pid = await child.WaitForOutputAsync("PID:", Token);
        await FeedTests.WaitBlockedAsync(
            connection,
            int.Parse(pid[4..], CultureInfo.InvariantCulture),
            Token
        );
        await child.StopAsync(Token);
        Assert.Contains("CANCELLED", child.StandardOutput, StringComparison.Ordinal);
        await transaction.CommitAsync(Token);
        await LedgerAsync(connection, 2, 5, Token);
        await using var services = Services(connection);
        var page = await Feed(services).ReadAsync(FeedCursor.Start(Location.FeedId), Limits, Token);
        Assert.Equal([2L, 3L], page.Events.Select(x => x.Position));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnacknowledgedCommitDoesNotRetryAndFreshStateDecidesRecovery(
        bool acknowledgedByProvider
    )
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using (var services = Services(connection, new CommitFault(acknowledgedByProvider)))
        {
            var result = await Feed(services).ProjectNextAsync(Ledger(), Limits, Token);
            Assert.Equal(ProjectionStatus.Indeterminate, result.Status);
            Assert.IsType<IOException>(result.Error);
        }

        await using var fresh = Services(connection);
        Assert.Equal(
            acknowledgedByProvider ? ProjectionStatus.NoWork : ProjectionStatus.Applied,
            (await Feed(fresh).ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
        await using var scope = fresh.CreateAsyncScope();
        Assert.Equal(
            10,
            (
                await scope
                    .ServiceProvider.GetRequiredService<Views>()
                    .Set<LedgerView>()
                    .SingleAsync(Token)
            ).Amount
        );
    }

    [Fact]
    public async Task NativeCheckpointFailureAfterProjectionSaveRollsBackBothAndFreshRecoveryWorks()
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await SqlAsync(
            connection,
            """
            CREATE FUNCTION feed.fail_checkpoint() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected native checkpoint failure'; END $$;
            CREATE TRIGGER fail_checkpoint BEFORE UPDATE ON feed.projection_checkpoints FOR EACH ROW EXECUTE FUNCTION feed.fail_checkpoint();
            """,
            Token
        );
        await using var services = Services(connection);
        var result = await Feed(services).ProjectNextAsync(Ledger(), Limits, Token);
        Assert.Equal(ProjectionStatus.Failed, result.Status);
        Assert.IsType<PostgresException>(result.Error);
        await using (var scope = services.CreateAsyncScope())
            Assert.Empty(
                await scope
                    .ServiceProvider.GetRequiredService<Views>()
                    .Set<LedgerView>()
                    .ToArrayAsync(Token)
            );

        await SqlAsync(
            connection,
            "DROP TRIGGER fail_checkpoint ON feed.projection_checkpoints",
            Token
        );
        Assert.Equal(
            ProjectionStatus.Applied,
            (await Feed(services).ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
    }

    [Theory]
    [InlineData("ALTER TABLE app.ledger ENABLE ROW LEVEL SECURITY")]
    [InlineData("ALTER TABLE app.ledger ALTER COLUMN stream_id DROP NOT NULL")]
    [InlineData(
        "ALTER TABLE app.reviews DROP CONSTRAINT reviews_pkey; ALTER TABLE app.reviews ADD PRIMARY KEY(id, tenant)"
    )]
    [InlineData(
        "INSERT INTO app.ledger VALUES('00000000-0000-0000-0000-000000000091', '00000000-0000-0000-0000-000000000082', 2, 'ledger.adjusted', 1, '{}')"
    )]
    public async Task UnsupportedNativeSourceShapeRollsBackInstallation(string change)
    {
        string connection = await SetupAsync(install: false);
        await SqlAsync(connection, change, Token);
        await Assert.ThrowsAsync<PostgresException>(() => InstallAsync(connection, Token));
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(Token);
        await using var query = new NpgsqlCommand(
            "SELECT to_regclass('feed.feed_head') IS NULL",
            session
        );
        Assert.Equal(true, await query.ExecuteScalarAsync(Token));
    }

    private async Task<string> SetupAsync(bool install = true)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await SchemaAsync(connection, Token);
        if (install)
            await InstallAsync(connection, Token);

        return connection;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivationWaitsForAnActualWriterAndRechecksAfterCommitOrCancellation(
        bool cancel
    )
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        var settings = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "feed_activation_proof",
        };
        await using var services = Services(settings.ConnectionString);
        var feed = Feed(services);
        await feed.ProjectNextAsync(Ledger(), Limits, Token);
        await feed.ProjectNextAsync(Ledger("v2"), Limits, Token);
        Assert.Equal(
            ProjectionStatus.Activated,
            (await feed.ActivateAsync("ledger", "v1", null, Token)).Status
        );
        await using var writer = new NpgsqlConnection(connection);
        await writer.OpenAsync(Token);
        await using var transaction = await writer.BeginTransactionAsync(Token);
        await LedgerAsync(writer, transaction, 2, 5, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var activating = feed.ActivateAsync("ledger", "v2", "v1", cancellation.Token);
        await WaitNamedLockAsync(connection, Token);
        if (cancel)
        {
            await cancellation.CancelAsync();
            Assert.Equal(ProjectionStatus.Cancelled, (await activating).Status);
        }

        await transaction.CommitAsync(Token);
        if (!cancel)
            Assert.Equal(ProjectionStatus.NotCaughtUp, (await activating).Status);

        await using (var observer = new NpgsqlConnection(connection))
        {
            await observer.OpenAsync(Token);
            await using var pointer = new NpgsqlCommand(
                "SELECT generation FROM feed.projection_active WHERE name = 'ledger'",
                observer
            );
            Assert.Equal("v1", await pointer.ExecuteScalarAsync(Token));
        }

        await feed.ProjectNextAsync(Ledger("v2"), Limits, Token);
        Assert.Equal(
            ProjectionStatus.Activated,
            (await feed.ActivateAsync("ledger", "v2", "v1", Token)).Status
        );
    }

    [Fact]
    public async Task TwoOperatorsUseTheExpectedPointerAndOneStatementReadersNeverMixGenerations()
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var services = Services(connection);
        var feed = Feed(services);
        foreach (string generation in new[] { "v1", "v2", "v3" })
            await feed.ProjectNextAsync(Ledger(generation), Limits, Token);

        await feed.ActivateAsync("ledger", "v1", null, Token);
        var results = await Task.WhenAll(
            feed.ActivateAsync("ledger", "v2", "v1", Token),
            feed.ActivateAsync("ledger", "v3", "v1", Token)
        );
        Assert.Equal(1, results.Count(x => x.Status == ProjectionStatus.Activated));
        Assert.Equal(1, results.Count(x => x.Status == ProjectionStatus.Conflict));
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(Token);
        await using var query = new NpgsqlCommand(
            "SELECT a.generation, v.generation, v.amount FROM feed.projection_active a JOIN app.ledger_view v ON v.generation = a.generation WHERE a.name = 'ledger'",
            observer
        );
        await using var reader = await query.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal(reader.GetString(0), reader.GetString(1));
        Assert.Equal(10, reader.GetInt32(2));
        Assert.False(await reader.ReadAsync(Token));
    }

    [Fact]
    public async Task ReaderStatementAcrossCutoverKeepsItsOriginalGenerationSnapshot()
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var services = Services(connection);
        var feed = Feed(services);
        await feed.ProjectNextAsync(Ledger(), Limits, Token);
        await feed.ProjectNextAsync(Ledger("v2"), Limits, Token);
        await feed.ActivateAsync("ledger", "v1", null, Token);
        await using var gate = new NpgsqlConnection(connection);
        await gate.OpenAsync(Token);
        await using var transaction = await gate.BeginTransactionAsync(Token);
        await using (
            var acquire = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(918273646::bigint)",
                gate,
                transaction
            )
        )
            await acquire.ExecuteNonQueryAsync(Token);

        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(Token);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(20));
        Task<(string Active, string Output, int Amount)> reading = ReadAsync(blocked: true);
        try
        {
            // PostgreSQL has established this statement's snapshot before reaching the held lock.
            await FeedTests.WaitBlockedAsync(connection, observer.ProcessID, Token);
            Assert.Equal(
                ProjectionStatus.Activated,
                (await feed.ActivateAsync("ledger", "v2", "v1", Token)).Status
            );
            await transaction.CommitAsync(Token);
            Assert.Equal(("v1", "v1", 10), await reading);
            Assert.Equal(("v2", "v2", 10), await ReadAsync(blocked: false));
        }
        finally
        {
            await lifetime.CancelAsync();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await reading.WaitAsync(cleanup.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }

        async Task<(string Active, string Output, int Amount)> ReadAsync(bool blocked)
        {
            await using var query = new NpgsqlCommand(
                (
                    blocked
                        ? "WITH boundary AS MATERIALIZED (SELECT pg_advisory_xact_lock(918273646::bigint)) "
                        : ""
                )
                    + "SELECT a.generation, v.generation, v.amount FROM "
                    + (blocked ? "boundary CROSS JOIN " : "")
                    + "feed.projection_active a JOIN app.ledger_view v ON v.generation = a.generation WHERE a.name = 'ledger'",
                observer
            );
            await using var reader = await query.ExecuteReaderAsync(lifetime.Token);
            Assert.True(await reader.ReadAsync(lifetime.Token));
            var selected = (reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
            Assert.False(await reader.ReadAsync(lifetime.Token));
            return selected;
        }
    }

    [Fact]
    public async Task ByteBudgetSplitsOneCommittedBatchWithoutReturningTheNextEnvelope()
    {
        string connection = await SetupAsync();
        await using (var writer = new NpgsqlConnection(connection))
        {
            await writer.OpenAsync(Token);
            await using var transaction = await writer.BeginTransactionAsync(Token);
            await LedgerAsync(writer, transaction, 1, 10, Token);
            await LedgerAsync(writer, transaction, 2, 20, Token);
            await transaction.CommitAsync(Token);
        }

        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(Token);
        await using var length = new NpgsqlCommand(
            "SELECT octet_length(envelope::text)::bigint FROM feed.feed_entries WHERE version = 1",
            observer
        );
        long bytes = (long)(await length.ExecuteScalarAsync(Token))!;
        await using var services = Services(connection);
        var first = await Feed(services)
            .ReadAsync(FeedCursor.Start(Location.FeedId), new(10, bytes), Token);
        Assert.Equal(1, Assert.Single(first.Events).Version);
        Assert.True(first.HasMore);
        var second = await Feed(services).ReadAsync(first.Next, new(10, bytes), Token);
        Assert.Equal(2, Assert.Single(second.Events).Version);
        Assert.False(second.HasMore);
    }

    private static async Task WaitNamedLockAsync(string connection, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(deadline.Token);
        while (true)
        {
            await using var query = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name = 'feed_activation_proof' AND wait_event_type = 'Lock')",
                observer
            );
            if ((bool)(await query.ExecuteScalarAsync(deadline.Token))!)
                return;

            await Task.Delay(20, deadline.Token);
        }
    }

    private static Dictionary<string, string> Environment(string connection) =>
        new() { ["FEED_CONNECTION"] = connection };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailurePreservesAcknowledgedCommitOrOriginalFailureAndDiscardsContext(
        bool apply
    )
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var services = Services(connection);
        var feed = new PostgresEventFeed<Views>(
            new CleanupScopes(services.GetRequiredService<IServiceScopeFactory>()),
            Location
        );
        Views? captured = null;
        var reducer = new ProjectionGeneration<Views>(
            "ledger",
            "v1",
            "ledger-v1",
            (database, generation, _, _) =>
            {
                captured = database;
                if (!apply)
                    throw new InvalidOperationException("owned poison reader");

                database.Add(
                    new LedgerView
                    {
                        Generation = generation,
                        Id = StreamId,
                        Amount = 10,
                    }
                );
                return Task.CompletedTask;
            }
        );
        var result = await feed.ProjectNextAsync(reducer, Limits, Token);
        Assert.Equal(apply ? ProjectionStatus.Applied : ProjectionStatus.Failed, result.Status);
        if (apply)
            Assert.IsType<IOException>(result.Error);
        else
            Assert.Equal(2, Assert.IsType<AggregateException>(result.Error).InnerExceptions.Count);

        Assert.NotNull(captured);
        Assert.Throws<ObjectDisposedException>(() => captured.ChangeTracker.Entries().ToArray());
        Assert.Equal(
            apply ? ProjectionStatus.NoWork : ProjectionStatus.Applied,
            (await Feed(services).ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivationCommitUncertaintyIsResolvedByFreshPointerObservation(bool accepted)
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var services = Services(connection);
        await Feed(services).ProjectNextAsync(Ledger(), Limits, Token);
        await using (var failing = Services(connection, new CommitFault(accepted)))
            Assert.Equal(
                ProjectionStatus.Indeterminate,
                (await Feed(failing).ActivateAsync("ledger", "v1", null, Token)).Status
            );

        if (accepted)
            await LedgerAsync(connection, 2, 5, Token);

        Assert.Equal(
            accepted ? ProjectionStatus.AlreadyActive : ProjectionStatus.Activated,
            (await Feed(services).ActivateAsync("ledger", "v1", null, Token)).Status
        );
    }

    private sealed class CleanupScopes(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new CleanupScope(inner.CreateScope());

        private sealed class CleanupScope(IServiceScope scope) : IServiceScope, IAsyncDisposable
        {
            public IServiceProvider ServiceProvider => scope.ServiceProvider;

            public void Dispose()
            {
                scope.Dispose();
                throw new IOException("owned scope cleanup failure");
            }

            public async ValueTask DisposeAsync()
            {
                if (scope is IAsyncDisposable asynchronous)
                    await asynchronous.DisposeAsync();
                else
                    scope.Dispose();

                throw new IOException("owned scope cleanup failure");
            }
        }
    }

    private sealed class CommitFault(bool accepted) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) =>
            accepted
                ? ValueTask.FromResult(result)
                : ValueTask.FromException<InterceptionResult>(
                    new IOException("Commit acknowledgment unavailable before provider call.")
                );

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromException(
                new IOException("Commit acknowledgment lost after provider completion.")
            );
    }

    [Theory]
    [InlineData("suppress")]
    [InlineData("accept")]
    [InlineData("detach")]
    [InlineData("original")]
    [InlineData("savepoint")]
    public async Task SuppressedProjectionSaveCannotAdvanceCheckpoint(string omission)
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        if (omission == "original")
        {
            await using var before = Services(connection);
            await Feed(before).ProjectNextAsync(Ledger(), Limits, Token);
            await LedgerAsync(connection, 2, 5, Token);
        }

        await using (var suppressed = Services(connection, new SuppressProjectionSave(omission)))
            Assert.Equal(
                ProjectionStatus.Failed,
                (await Feed(suppressed).ProjectNextAsync(Ledger(), Limits, Token)).Status
            );

        await using var fresh = Services(connection);
        Assert.Equal(
            ProjectionStatus.Applied,
            (await Feed(fresh).ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
        await using var scope = fresh.CreateAsyncScope();
        Assert.Equal(
            omission == "original" ? 15 : 10,
            (
                await scope
                    .ServiceProvider.GetRequiredService<Views>()
                    .Set<LedgerView>()
                    .SingleAsync(Token)
            ).Amount
        );
    }

    private sealed class SuppressProjectionSave(string omission) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var database = eventData.Context!;
            if (omission == "suppress")
                return InterceptionResult<int>.SuppressWithResult(0);

            if (omission == "accept")
                database.ChangeTracker.AcceptAllChanges();
            else if (omission == "detach")
                database.ChangeTracker.Clear();
            else if (omission == "savepoint")
                await database.Database.CurrentTransaction!.CreateSavepointAsync(
                    "output_guard",
                    cancellationToken
                );
            else
                foreach (var entry in database.ChangeTracker.Entries<LedgerView>())
                {
                    entry.Property(x => x.Amount).OriginalValue = entry
                        .Property(x => x.Amount)
                        .CurrentValue;
                    entry.Property(x => x.Amount).IsModified = false;
                }

            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (omission == "savepoint")
                await eventData.Context!.Database.CurrentTransaction!.RollbackToSavepointAsync(
                    "output_guard",
                    cancellationToken
                );

            return result;
        }
    }

    [Fact]
    public async Task ExplicitlyEnlistedAndAlreadyOpenReadContextsCannotExposeTheirUncommittedCapture()
    {
        string connection = await SetupAsync();
        await using var native = new NpgsqlConnection(connection);
        await native.OpenAsync(Token);
        using var enlisted = new System.Transactions.CommittableTransaction();
        native.EnlistTransaction(enlisted);
        await using (
            var insert = new NpgsqlCommand(
                """
                INSERT INTO app.ledger VALUES('00000000-0000-0000-0000-000000000091',
                    '00000000-0000-0000-0000-000000000082', 1, 'ledger.adjusted', 1, '{"amount":10}')
                """,
                native
            )
        )
            await insert.ExecuteNonQueryAsync(Token);

        await using var services = new ServiceCollection()
            .AddScoped(_ =>
            {
                var context = new Views(
                    new DbContextOptionsBuilder<Views>().UseNpgsql(native).Options
                );
                context.Database.EnlistTransaction(enlisted);
                return context;
            })
            .BuildServiceProvider();
        Assert.Null(System.Transactions.Transaction.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Feed(services).ReadAsync(FeedCursor.Start(Location.FeedId), Limits, Token)
        );
        Assert.Equal(
            ProjectionStatus.Failed,
            (await Feed(services).ProjectNextAsync(Ledger(), Limits, Token)).Status
        );
        enlisted.Rollback();
        await using var fresh = Services(connection);
        Assert.Empty(
            (await Feed(fresh).ReadAsync(FeedCursor.Start(Location.FeedId), Limits, Token)).Events
        );
    }

    [Fact]
    public async Task NativeProjectionDeletionIsVerifiedBeforeItsCheckpointCommits()
    {
        string connection = await SetupAsync();
        await LedgerAsync(connection, 1, 10, Token);
        await using var services = Services(connection);
        await Feed(services).ProjectNextAsync(Ledger(), Limits, Token);
        await LedgerAsync(connection, 2, 5, Token);
        var remove = new ProjectionGeneration<Views>(
            "ledger",
            "v1",
            "ledger-v1",
            async (db, _, _, ct) => db.Remove(await db.Set<LedgerView>().SingleAsync(ct))
        );
        Assert.Equal(
            ProjectionStatus.Applied,
            (await Feed(services).ProjectNextAsync(remove, Limits, Token)).Status
        );
        Assert.Equal(
            ProjectionStatus.NoWork,
            (await Feed(services).ProjectNextAsync(remove, Limits, Token)).Status
        );
    }

    [Fact]
    public async Task LargestSupportedEscapedKeysReturnARestorableIntraBatchCursor()
    {
        string connection = await SetupAsync(install: false);
        await SqlAsync(
            connection,
            "CREATE TABLE app.large(id text PRIMARY KEY, stream text NOT NULL, version bigint NOT NULL)",
            Token
        );
        await using (var native = new NpgsqlConnection(connection))
        {
            await native.OpenAsync(Token);
            await using var transaction = await native.BeginTransactionAsync(Token);
            await using var install = new NpgsqlCommand(
                EventFeedInstallation.Create(
                    Location,
                    [new EventFeedSource("large", "app", "large", ["stream"], "version", ["id"])]
                ),
                native,
                transaction
            );
            await install.ExecuteNonQueryAsync(Token);
            await using var insert = new NpgsqlCommand(
                "INSERT INTO app.large VALUES(repeat('<', 500), repeat('<', 500), 1), (repeat('>', 500), repeat('<', 500), 2)",
                native,
                transaction
            );
            await insert.ExecuteNonQueryAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using var services = Services(connection);
        var feed = Feed(services);
        var first = await feed.ReadAsync(
            FeedCursor.Start(Location.FeedId),
            new(1, 1024 * 1024),
            Token
        );
        Assert.True(first.HasMore);
        Assert.True(first.Next.Value.Length > 8192);
        var second = await feed.ReadAsync(FeedCursor.Parse(first.Next.Value), Limits, Token);
        Assert.Equal(2, Assert.Single(second.Events).Version);
        Assert.False(second.HasMore);
    }
}
