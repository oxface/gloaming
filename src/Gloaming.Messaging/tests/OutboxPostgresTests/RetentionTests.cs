using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;
using static Gloaming.Messaging.Tests.OutboxRecoveryProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class RetentionTests(PostgreSqlFixture postgres)
{
    private static readonly DateTimeOffset Cutoff = new(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BatchUsesStrictCompletionCutoffAndPreservesOtherLifecycleStatesAndRoutes()
    {
        string connection = await DatabaseAsync(postgres);
        DateTimeOffset cutoff = new(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var old = await EnqueueAsync(connection);
        var boundary = await EnqueueAsync(connection);
        var pending = await EnqueueAsync(connection);
        var delayed = await EnqueueAsync(connection);
        var expired = await EnqueueAsync(connection);
        var leased = await EnqueueAsync(connection);
        var stopped = await EnqueueAsync(connection);
        var other = await EnqueueAsync(
            connection,
            new(Guid.NewGuid(), "other", "render", 1, Message().Payload)
        );
        await SqlAsync(
            connection,
            $"""
            UPDATE messages.pending SET dispatched_at = '2020-01-01Z' WHERE message_id IN ('{old.MessageId}', '{other.MessageId}');
            UPDATE messages.pending SET dispatched_at = '2020-01-02Z' WHERE message_id = '{boundary.MessageId}';
            UPDATE messages.pending SET available_at = clock_timestamp() + interval '1 hour' WHERE message_id = '{delayed.MessageId}';
            UPDATE messages.pending SET lease_token = gen_random_uuid(), lease_until = clock_timestamp() - interval '1 hour',
                attempts = 1, revision = 1 WHERE message_id = '{expired.MessageId}';
            UPDATE messages.pending SET lease_token = gen_random_uuid(), lease_until = clock_timestamp() + interval '1 hour',
                attempts = 1, revision = 1 WHERE message_id = '{leased.MessageId}';
            UPDATE messages.pending SET attempts = 1, failure_count = 1, failures_since_redrive = 1,
                last_failed_at = clock_timestamp(), last_failure_code = 'review', dead_lettered_at = clock_timestamp()
                WHERE message_id = '{stopped.MessageId}';
            """
        );
        await using var database = Context(connection);
        Assert.Equal(
            1,
            await database.DeleteCompletedPostgresOutboxBatchAsync("exports", cutoff, 10, Token)
        );
        var rows = await database
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .Select(row => row.MessageId)
            .ToArrayAsync(Token);
        Assert.DoesNotContain(old.MessageId, rows);
        Assert.Equal(7, rows.Length);
        Assert.Contains(pending.MessageId, rows);
        Assert.Contains(delayed.MessageId, rows);
        Assert.Contains(expired.MessageId, rows);
        Assert.Contains(boundary.MessageId, rows);
        Assert.Contains(leased.MessageId, rows);
        Assert.Contains(stopped.MessageId, rows);
        Assert.Contains(other.MessageId, rows);
    }

    [Fact]
    public async Task EqualCompletionTimesUseUuidOrderingAndRepeatedCallsStayBounded()
    {
        string connection = await DatabaseAsync(postgres);
        Guid[] ids =
        [
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
        ];
        foreach (Guid id in ids.Reverse())
            await EnqueueAsync(connection, Message(id));

        await CompleteAllAsync(connection);
        await using var database = Context(connection);
        Assert.Equal(
            2,
            await database.DeleteCompletedPostgresOutboxBatchAsync(
                "exports",
                Cutoff.ToOffset(TimeSpan.FromHours(3)),
                2,
                Token
            )
        );
        Assert.Equal(
            ids[2],
            (await database.Set<OutboxMessageRecord>().AsNoTracking().SingleAsync(Token)).MessageId
        );
        Assert.Equal(
            1,
            await database.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 2, Token)
        );
        Assert.Equal(
            0,
            await database.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 2, Token)
        );
        await EnqueueAsync(connection, Message(ids[0]));
        Assert.Equal(1, await database.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitRejectionRollsBackButLostResponseAfterRealCommitLeavesDeletionApplied(
        bool responseLost
    )
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        await CompleteAllAsync(connection);
        if (!responseLost)
            await SqlAsync(
                connection,
                """
                CREATE FUNCTION messages.reject_retention_commit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'retention commit rejected'; END; $$;
                CREATE CONSTRAINT TRIGGER retention_commit AFTER DELETE ON messages.pending
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION messages.reject_retention_commit();
                """
            );

        await using var database = new OutboxConsumer(
            new DbContextOptionsBuilder<OutboxConsumer>()
                .UseNpgsql(connection)
                .AddInterceptors(new LostCommitResponse(1))
                .Options
        );
        if (responseLost)
            await Assert.ThrowsAsync<IOException>(() =>
                database.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 1, Token)
            );
        else
            await Assert.ThrowsAsync<PostgresException>(() =>
                database.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 1, Token)
            );

        await using var fresh = Context(connection);
        Assert.Equal(
            responseLost ? 0 : 1,
            await fresh.Set<OutboxMessageRecord>().CountAsync(Token)
        );
        if (!responseLost)
            await SqlAsync(connection, "DROP TRIGGER retention_commit ON messages.pending");

        Assert.Equal(
            responseLost ? 0 : 1,
            await fresh.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 1, Token)
        );
    }

    [Fact]
    public async Task CompetingCleanersWaitOnTheSameCandidatesWhileUnrelatedDispatchProgresses()
    {
        string connection = await DatabaseAsync(postgres);
        for (int i = 0; i < 3; i++)
            await EnqueueAsync(connection);

        await CompleteAllAsync(connection);
        var pending = await EnqueueAsync(connection);
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync(Token);
        await using (
            var install = new NpgsqlCommand(
                """
                SELECT pg_advisory_lock(281001);
                CREATE FUNCTION messages.hold_retention() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF current_setting('application_name') = 'cleaner-one' THEN PERFORM pg_advisory_xact_lock(281001); END IF;
                    RETURN OLD;
                END; $$;
                CREATE TRIGGER hold_retention BEFORE DELETE ON messages.pending FOR EACH ROW EXECUTE FUNCTION messages.hold_retention();
                """,
                barrier
            )
        )
            await install.ExecuteNonQueryAsync(Token);

        await using var first = Context(Named(connection, "cleaner-one"));
        await using var second = Context(Named(connection, "cleaner-two"));
        var winner = first.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 2, Token);
        Task<int>? competitor = null;
        try
        {
            await WaitAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'cleaner-one' AND wait_event = 'advisory')"
            );
            competitor = second.DeleteCompletedPostgresOutboxBatchAsync(
                "exports",
                Cutoff,
                2,
                Token
            );
            await WaitAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'cleaner-two' AND cardinality(pg_blocking_pids(pid)) > 0)"
            );
            await using var dispatch = Context(connection);
            Assert.Equal(
                OutboxDispatchResult.Published,
                await Dispatcher(dispatch, new()).DispatchNextAsync(Token)
            );
        }
        finally
        {
            await using var release = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(281001)",
                barrier
            );
            await release.ExecuteNonQueryAsync(Token);
        }

        Assert.Equal(2, await winner);
        Assert.Equal(0, await competitor!);
        await using var fresh = Context(connection);
        Assert.Equal(
            1,
            await fresh.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 2, Token)
        );
        Assert.Equal(
            pending.MessageId,
            (await fresh.Set<OutboxMessageRecord>().SingleAsync(Token)).MessageId
        );
    }

    [Fact]
    public async Task CancellationDuringDeleteLeavesCompletedWorkForFreshContextRecovery()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        await CompleteAllAsync(connection);
        await using var blocker = new NpgsqlConnection(connection);
        await blocker.OpenAsync(Token);
        await using var locked = await blocker.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "SELECT message_id FROM messages.pending FOR UPDATE",
                blocker,
                locked
            )
        )
            await command.ExecuteScalarAsync(Token);
        await using var database = Context(Named(connection, "cancel-retention"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var operation = database.DeleteCompletedPostgresOutboxBatchAsync(
            "exports",
            Cutoff,
            1,
            cancellation.Token
        );
        await WaitAsync(
            connection,
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'cancel-retention' AND cardinality(pg_blocking_pids(pid)) > 0)"
        );
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        await locked.RollbackAsync(Token);
        await using var fresh = Context(connection);
        Assert.Equal(
            1,
            await fresh.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 1, Token)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObsoletePublisherMayFinishExternallyAfterCleanupButCannotRecreateOrAccountTheRow(
        bool publicationFails
    )
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                accepted.SetResult();
                if (publicationFails)
                    throw new IOException("response lost after late acceptance");
            },
        };
        await using var old = Context(connection);
        var obsolete = Dispatcher(old, publisher).DispatchNextAsync(Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await ExpireAsync(connection);
            await using var successor = Context(connection);
            Assert.Equal(
                OutboxDispatchResult.Published,
                await Dispatcher(successor, new()).DispatchNextAsync(Token)
            );
            await CompleteAllAsync(connection);
            Assert.Equal(
                1,
                await successor.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 1, Token)
            );
            release.SetResult();
            if (publicationFails)
                await Assert.ThrowsAsync<IOException>(() => obsolete);
            else
                Assert.Equal(OutboxDispatchResult.ClaimLost, await obsolete);

            Assert.True(accepted.Task.IsCompletedSuccessfully);
            Assert.Equal(message.MessageId, Assert.Single(publisher.Messages).MessageId);
            Assert.Empty(await successor.Set<OutboxMessageRecord>().ToArrayAsync(Token));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task InvalidBatchSizeRejectsBeforeDatabaseWork(int batchSize)
    {
        await using var database = Context("Host=127.0.0.1;Port=1;Database=unused;Username=unused");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, batchSize, Token)
        );
    }

    [Fact]
    public async Task FutureCutoffCannotDeleteAndCallerTransactionsAreRejected()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        await CompleteAllAsync(connection);
        await using var database = Context(connection);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.DeleteCompletedPostgresOutboxBatchAsync(
                "exports",
                DateTimeOffset.MaxValue,
                1,
                Token
            )
        );
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 1, Token)
        );
        Assert.Equal(1, await database.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task MetadataOnlyMaintenanceWorksWithoutUpdateAndNativeRlsLimitsDeletion()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        await EnqueueAsync(
            connection,
            new(Guid.NewGuid(), "exports", "render", 1, Message().Payload, "tenant-beta")
        );
        await CompleteAllAsync(connection);
        await EnqueueAsync(connection);
        string role = "retention_" + Guid.NewGuid().ToString("N");
        await SqlAsync(
            connection,
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD 'retention-proof';
            GRANT USAGE ON SCHEMA messages TO "{role}";
            GRANT SELECT(message_id, destination, dispatched_at, dead_lettered_at, lease_token, lease_until), DELETE
                ON messages.pending TO "{role}";
            ALTER TABLE messages.pending ENABLE ROW LEVEL SECURITY;
            CREATE POLICY admitted_read ON messages.pending FOR SELECT TO "{role}" USING (owner_key = 'tenant-alpha');
            CREATE POLICY completed_delete ON messages.pending FOR DELETE TO "{role}" USING
                (owner_key = 'tenant-alpha' AND dispatched_at IS NOT NULL AND dead_lettered_at IS NULL AND lease_token IS NULL AND lease_until IS NULL);
            """
        );
        string restricted = new NpgsqlConnectionStringBuilder(connection)
        {
            Username = role,
            Password = "retention-proof",
        }.ConnectionString;
        await using var database = Context(restricted);
        Assert.Equal(
            1,
            await database.DeleteCompletedPostgresOutboxBatchAsync("exports", Cutoff, 10, Token)
        );
        await using var native = new NpgsqlConnection(restricted);
        await native.OpenAsync(Token);
        foreach (
            string sql in new[]
            {
                "SELECT payload FROM messages.pending",
                "UPDATE messages.pending SET attempts = 1",
                "SELECT * FROM messages.business",
            }
        )
        {
            await using var command = new NpgsqlCommand(sql, native);
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                command.ExecuteNonQueryAsync(Token)
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }

        await using (
            var bypass = new NpgsqlCommand(
                "DELETE FROM messages.pending WHERE dispatched_at IS NULL",
                native
            )
        )
            Assert.Equal(0, await bypass.ExecuteNonQueryAsync(Token));
        await using var owner = Context(connection);
        Assert.Equal(2, await owner.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    private static Task CompleteAllAsync(string connection) =>
        SqlAsync(
            connection,
            "UPDATE messages.pending SET dispatched_at = '2020-01-01Z', lease_token = NULL, lease_until = NULL WHERE dead_lettered_at IS NULL"
        );

    private static string Named(string connection, string name) =>
        new NpgsqlConnectionStringBuilder(connection) { ApplicationName = name }.ConnectionString;
}
