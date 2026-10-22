using System.Data.Common;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed partial class RetentionTests
{
    [Theory(Timeout = 60_000)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task IntakeWaitsForDeletionAndObservesCommittedExpiryOrRetainedContent(
        bool deleteCommits,
        bool changed
    )
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await DatabaseAsync(postgres);
        var message = Message();
        await using (var initial = Provider(connection))
            await ReceiveAsync(initial, message);

        await SqlAsync(connection, "UPDATE receiver.inbox SET processed_at = '2020-01-01Z'");
        await using var deleting = new NpgsqlConnection(connection);
        await deleting.OpenAsync(token);
        await using var transaction = await deleting.BeginTransactionAsync(token);
        await using (
            var command = new NpgsqlCommand(
                "DELETE FROM receiver.inbox WHERE processed_at IS NOT NULL",
                deleting,
                transaction
            )
        )
            Assert.Equal(1, await command.ExecuteNonQueryAsync(token));

        await using var provider = Provider(Named(connection, "intake-delete-wait"));
        var arrival = ReceiveAsync(
            provider,
            changed ? Message(message.MessageId, "{\"value\":2}") : message
        );
        await AwaitBackendAsync(connection, "intake-delete-wait");
        if (deleteCommits)
            await transaction.CommitAsync(token);
        else
            await transaction.RollbackAsync(token);

        if (!deleteCommits && changed)
            await Assert.ThrowsAsync<InboxMessageConflictException>(() => arrival);
        else
            Assert.Equal(
                deleteCommits ? InboxReceiveResult.Queued : InboxReceiveResult.AlreadyReceived,
                await arrival
            );

        await using var read = Context(connection);
        var retained = await read.Set<InboxMessageRecord>().SingleAsync(token);
        Assert.Equal(deleteCommits, retained.ProcessedAt is null);
    }

    [Fact(Timeout = 60_000)]
    public async Task CompetingCleanersWaitOnTheSameBatchAndUnrelatedProcessingProgresses()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await DatabaseAsync(postgres);
        await using var provider = Provider(connection);
        for (int i = 0; i < 3; i++)
            await ReceiveAsync(provider, Message());

        await SqlAsync(connection, "UPDATE receiver.inbox SET processed_at = '2020-01-01Z'");
        var pending = Message();
        await ReceiveAsync(provider, pending);
        await using var gate = await HoldSqlAsync(
            connection,
            """
            SELECT pg_advisory_lock(282002);
            CREATE FUNCTION receiver.hold_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF current_setting('application_name') = 'cleaner-first' THEN
                    PERFORM pg_advisory_xact_lock(282002);
                END IF;
                RETURN OLD;
            END; $$;
            CREATE TRIGGER hold_delete BEFORE DELETE ON receiver.inbox FOR EACH ROW EXECUTE FUNCTION receiver.hold_delete();
            """
        );
        await using var first = Context(Named(connection, "cleaner-first"));
        await using var second = Context(Named(connection, "cleaner-second"));
        var firstCall = first.DeleteProcessedPostgresInboxBatchAsync(
            "render",
            "exports",
            Cutoff,
            1,
            token
        );
        await AwaitBackendAsync(connection, "cleaner-first", "advisory");
        var secondCall = second.DeleteProcessedPostgresInboxBatchAsync(
            "render",
            "exports",
            Cutoff,
            1,
            token
        );
        await AwaitBackendAsync(connection, "cleaner-second");
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await ReleaseSqlAsync(gate, 282002);
        Assert.Equal(1, await firstCall);
        Assert.Equal(0, await secondCall);
        await using var fresh = Context(connection);
        Assert.Equal(
            2,
            await fresh.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                10,
                token
            )
        );
        Assert.Equal(
            pending.MessageId,
            (await fresh.Set<InboxMessageRecord>().AsNoTracking().SingleAsync(token)).MessageId
        );
        Assert.Single(await fresh.Set<HandledItem>().ToArrayAsync(token));
    }

    [Theory(Timeout = 60_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupPreservesLockedProcessingAndUncommittedCompletionIncludingRollback(
        bool rejectCompletion
    )
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await DatabaseAsync(postgres);
        await using var intake = Provider(connection);
        await ReceiveAsync(intake, Message());
        await SqlAsync(connection, "UPDATE receiver.inbox SET processed_at = '2020-01-01Z'");
        var active = Message();
        await ReceiveAsync(intake, active);
        await using var gate = await HoldSqlAsync(
            connection,
            """
            SELECT pg_advisory_lock(282003);
            CREATE FUNCTION receiver.hold_completion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM pg_advisory_xact_lock(282003);
                IF current_setting('application_name') = 'processing-rollback' THEN
                    RAISE EXCEPTION 'reject completion';
                END IF;
                RETURN NEW;
            END; $$;
            CREATE TRIGGER hold_completion BEFORE UPDATE ON receiver.inbox FOR EACH ROW
                WHEN (NEW.processed_at IS NOT NULL) EXECUTE FUNCTION receiver.hold_completion();
            """
        );
        string name = rejectCompletion ? "processing-rollback" : "processing-commit";
        await using var processor = Provider(Named(connection, name));
        var processing = ProcessAsync(processor);
        await AwaitBackendAsync(connection, name, "advisory");
        await using var maintenance = Context(connection);
        Assert.Equal(
            1,
            await maintenance.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                10,
                token
            )
        );
        Assert.Equal(
            0,
            await maintenance.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                10,
                token
            )
        );
        Assert.Null(
            (
                await maintenance.Set<InboxMessageRecord>().AsNoTracking().SingleAsync(token)
            ).ProcessedAt
        );
        await ReleaseSqlAsync(gate, 282003);
        if (rejectCompletion)
            await Assert.ThrowsAsync<PostgresException>(() => processing);
        else
            Assert.Equal(InboxProcessingResult.Processed, await processing);

        DateTimeOffset after = await DatabaseNowAsync(maintenance);
        Assert.Equal(
            rejectCompletion ? 0 : 1,
            await maintenance.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                after,
                10,
                token
            )
        );
        Assert.Equal(
            rejectCompletion ? 0 : 1,
            await maintenance.Set<HandledItem>().CountAsync(token)
        );
        if (rejectCompletion)
        {
            var retained = await maintenance
                .Set<InboxMessageRecord>()
                .AsNoTracking()
                .SingleAsync(token);
            Assert.Equal(active.MessageId, retained.MessageId);
            Assert.Null(retained.ProcessedAt);
            Assert.Equal(1, retained.FailureCount);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task CleanupCannotRemoveDeadLettersOrWorkDuringAndAfterOperatorRetry()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await DatabaseAsync(postgres);
        await using var intake = Provider(connection);
        var message = Message();
        await ReceiveAsync(intake, message);
        await SqlAsync(
            connection,
            """
            UPDATE receiver.inbox SET received_at = '2019-01-01Z', failure_count = 1, failures_since_redrive = 1,
                revision = 1, last_failed_at = '2020-01-01Z', last_failure_code = 'review', quarantined_at = '2020-01-01Z';
            """
        );
        var services = Services(Named(connection, "operator-retention"));
        services.AddPostgresInboxDeadLetters<InboxConsumer>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        var observed = (await letters.GetAsync("render", "exports", message.MessageId, token))!;
        await using var gate = await HoldSqlAsync(
            connection,
            """
            SELECT pg_advisory_lock(282004);
            CREATE FUNCTION receiver.hold_operator() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN PERFORM pg_advisory_xact_lock(282004); RETURN NEW; END; $$;
            CREATE TRIGGER hold_operator BEFORE UPDATE ON receiver.inbox FOR EACH ROW
                WHEN (OLD.quarantined_at IS NOT NULL AND NEW.quarantined_at IS NULL)
                EXECUTE FUNCTION receiver.hold_operator();
            """
        );
        var retry = letters.RetryAsync(
            "render",
            "exports",
            message.MessageId,
            observed.ReceiptId,
            observed.Revision,
            token
        );
        await AwaitBackendAsync(connection, "operator-retention", "advisory");
        await using var maintenance = Context(connection);
        Assert.Equal(
            0,
            await maintenance.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                10,
                token
            )
        );
        await ReleaseSqlAsync(gate, 282004);
        Assert.Equal(InboxRetryResult.Retried, await retry);
        Assert.Equal(
            0,
            await maintenance.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                10,
                token
            )
        );
        var row = await maintenance.Set<InboxMessageRecord>().AsNoTracking().SingleAsync(token);
        Assert.Equal(observed.ReceiptId, row.ReceiptId);
        Assert.Null(row.ProcessedAt);
        Assert.Null(row.DeadLetteredAt);
        Assert.Equal(1, row.RedriveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitRejectionRollsBackAndLostResponseAfterRealCommitLeavesDeletionApplied(
        bool responseLost
    )
    {
        string connection = await DatabaseAsync(postgres);
        await using var provider = Provider(connection);
        var message = Message();
        await ReceiveAsync(provider, message);
        await SqlAsync(connection, "UPDATE receiver.inbox SET processed_at = '2020-01-01Z'");
        if (!responseLost)
            await SqlAsync(
                connection,
                """
                CREATE FUNCTION receiver.reject_cleanup() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'reject cleanup commit'; END; $$;
                CREATE CONSTRAINT TRIGGER reject_cleanup AFTER DELETE ON receiver.inbox
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION receiver.reject_cleanup();
                """
            );

        await using var database = new InboxConsumer(
            new DbContextOptionsBuilder<InboxConsumer>()
                .UseNpgsql(connection)
                .AddInterceptors(new LostCleanupCommitResponse())
                .Options
        );
        if (responseLost)
            await Assert.ThrowsAsync<IOException>(() =>
                database.DeleteProcessedPostgresInboxBatchAsync(
                    "render",
                    "exports",
                    Cutoff,
                    1,
                    Token
                )
            );
        else
            await Assert.ThrowsAsync<PostgresException>(() =>
                database.DeleteProcessedPostgresInboxBatchAsync(
                    "render",
                    "exports",
                    Cutoff,
                    1,
                    Token
                )
            );

        await using var fresh = Context(connection);
        Assert.Equal(responseLost ? 0 : 1, await fresh.Set<InboxMessageRecord>().CountAsync(Token));
        if (!responseLost)
            await SqlAsync(connection, "DROP TRIGGER reject_cleanup ON receiver.inbox");

        Assert.Equal(
            responseLost ? 0 : 1,
            await fresh.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                1,
                Token
            )
        );
    }

    private static string Named(string connection, string name) =>
        new NpgsqlConnectionStringBuilder(connection) { ApplicationName = name }.ConnectionString;

    private static async Task<NpgsqlConnection> HoldSqlAsync(string connection, string sql)
    {
        var native = new NpgsqlConnection(connection);
        try
        {
            await native.OpenAsync(Token);
            await using var command = new NpgsqlCommand(sql, native);
            await command.ExecuteNonQueryAsync(Token);
            return native;
        }
        catch
        {
            await native.DisposeAsync();
            throw;
        }
    }

    private static async Task ReleaseSqlAsync(NpgsqlConnection connection, int key)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static Task AwaitBackendAsync(
        string connection,
        string name,
        string? waitEvent = null
    ) =>
        WaitAsync(async () =>
        {
            await using var observer = new NpgsqlConnection(connection);
            await observer.OpenAsync(Token);
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND application_name = @name AND wait_event_type = 'Lock' AND (@event IS NULL OR wait_event = @event))",
                observer
            );
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue(
                "event",
                NpgsqlTypes.NpgsqlDbType.Text,
                (object?)waitEvent ?? DBNull.Value
            );
            return await command.ExecuteScalarAsync(Token) is true;
        });

    private static async Task<DateTimeOffset> DatabaseNowAsync(DbContext database) =>
        new(
            await database
                .Database.SqlQueryRaw<DateTime>("SELECT clock_timestamp() AS \"Value\"")
                .SingleAsync(Token)
        );

    private sealed class LostCleanupCommitResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) => throw new IOException("response lost after actual PostgreSQL commit");
    }
}
