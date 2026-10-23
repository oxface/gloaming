using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed partial class RetentionTests(PostgreSqlFixture postgres)
{
    private static readonly DateTimeOffset Cutoff = new(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OldOperatorRequestCannotRetryARecreatedReceiptAtTheSameRevision()
    {
        string connection = await DatabaseAsync(postgres);
        var scenario = new HandlerScenario
        {
            OnHandle = (_, _, _) => throw new IOException("requires repair"),
        };
        var services = Services(connection, scenario);
        services.AddSingleton(
            new FailureRecoveryTests.FailureScenario
            {
                Decide = _ => new InboxFailureDecision.DeadLetter("review"),
            }
        );
        services.AddInboxFailurePolicy<InboxConsumer, FailureRecoveryTests.Policy>("render");
        services.AddPostgresInboxDeadLetters<InboxConsumer>();
        await using var provider = services.BuildServiceProvider();
        var message = Message();
        await ReceiveAsync(provider, message);
        await Assert.ThrowsAsync<IOException>(() => ProcessAsync(provider));
        await using var scope = provider.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        var original = (await letters.GetAsync("render", "exports", message.MessageId, Token))!;
        Assert.Equal(
            InboxRetryResult.Retried,
            await letters.RetryAsync(
                "render",
                "exports",
                message.MessageId,
                original.ReceiptId,
                original.Revision,
                Token
            )
        );
        scenario.OnHandle = null;
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await SqlAsync(connection, "UPDATE receiver.inbox SET processed_at = '2020-01-01Z'");
        await using var maintenance = Context(connection);
        Assert.Equal(
            1,
            await maintenance.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                1,
                Token
            )
        );
        Assert.Equal(InboxReceiveResult.Queued, await ReceiveAsync(provider, message));
        scenario.OnHandle = (_, _, _) =>
            throw new IOException("later receipt requires a new decision");
        await Assert.ThrowsAsync<IOException>(() => ProcessAsync(provider));
        var recreated = (await letters.GetAsync("render", "exports", message.MessageId, Token))!;
        Assert.Equal(original.Revision, recreated.Revision);
        Assert.NotEqual(original.ReceiptId, recreated.ReceiptId);
        Assert.NotEqual(Guid.Empty, recreated.ReceiptId);
        Assert.Equal(
            InboxRetryResult.NotApplied,
            await letters.RetryAsync(
                "render",
                "exports",
                message.MessageId,
                original.ReceiptId,
                original.Revision,
                Token
            )
        );
        Assert.Equal(
            InboxRetryResult.NotApplied,
            await letters.RetryAsync(
                "render",
                "exports",
                message.MessageId,
                original.ReceiptId,
                original.Revision,
                Token
            )
        );
        Assert.Equal(
            InboxRetryResult.Retried,
            await letters.RetryAsync(
                "render",
                "exports",
                message.MessageId,
                recreated.ReceiptId,
                recreated.Revision,
                Token
            )
        );
        Assert.Equal(
            InboxRetryResult.NotApplied,
            await letters.RetryAsync(
                "render",
                "exports",
                message.MessageId,
                recreated.ReceiptId,
                recreated.Revision,
                Token
            )
        );
        await using var fresh = Context(connection);
        var row = await fresh.Set<InboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(recreated.ReceiptId, row.ReceiptId);
        Assert.Equal(1, row.FailureCount);
        Assert.Equal(1, row.RedriveCount);
        Assert.Equal(0, row.FailuresSinceRedrive);
    }

    [Fact]
    public async Task CleanupDeletesOnlyProcessedReceiptsInExactLaneBeforeCutoff()
    {
        string connection = await DatabaseAsync(postgres);
        await using var provider = Provider(connection);
        var old = Message();
        var boundary = Message();
        var pending = Message();
        var delayed = Message();
        var stopped = Message();
        var otherProducer = Message(producer: "other");
        var otherSubscription = Message();
        foreach (var message in new[] { old, boundary, pending, delayed, stopped, otherProducer })
            await ReceiveAsync(provider, message);

        await ReceiveAsync(provider, otherSubscription, "another");
        await SqlAsync(
            connection,
            $"""
            UPDATE receiver.inbox SET processed_at = '2020-01-01Z'
                WHERE message_id IN ('{old.MessageId}', '{otherProducer.MessageId}', '{otherSubscription.MessageId}');
            UPDATE receiver.inbox SET processed_at = '2020-01-02Z' WHERE message_id = '{boundary.MessageId}';
            UPDATE receiver.inbox SET available_at = clock_timestamp() + interval '1 hour'
                WHERE message_id = '{delayed.MessageId}';
            UPDATE receiver.inbox SET failure_count = 1, failures_since_redrive = 1, revision = 1,
                last_failed_at = clock_timestamp(), last_failure_code = 'review', quarantined_at = clock_timestamp()
                WHERE message_id = '{stopped.MessageId}';
            """
        );
        await using var database = Context(connection);
        Assert.Equal(
            1,
            await database.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                10,
                Token
            )
        );
        var survivors = await database
            .Set<InboxMessageRecord>()
            .AsNoTracking()
            .Select(row => row.MessageId)
            .ToArrayAsync(Token);
        Assert.Equal(6, survivors.Length);
        Assert.DoesNotContain(old.MessageId, survivors);
        Assert.All(
            new[] { boundary, pending, delayed, stopped, otherProducer, otherSubscription },
            message => Assert.Contains(message.MessageId, survivors)
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task IntakeCanQueueAgainWhenCleanupRemovesTheConflictingReceiptBeforeComparison()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await DatabaseAsync(postgres);
        var message = Message();
        await using (var initial = Provider(connection))
        {
            await ReceiveAsync(initial, message);
            Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(initial));
        }

        await using var gate = new NpgsqlConnection(connection);
        await gate.OpenAsync(token);
        await using (
            var install = new NpgsqlCommand(
                """
                SELECT pg_advisory_lock(282001);
                CREATE FUNCTION receiver.pause_intake() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF current_setting('application_name') = 'intake-delete-gap' THEN
                        PERFORM pg_advisory_xact_lock(282001);
                    END IF;
                    RETURN NULL;
                END; $$;
                CREATE TRIGGER pause_intake AFTER INSERT ON receiver.inbox
                    FOR EACH STATEMENT EXECUTE FUNCTION receiver.pause_intake();
                """,
                gate
            )
        )
            await install.ExecuteNonQueryAsync(Token);

        string named = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "intake-delete-gap",
        }.ConnectionString;
        await using var contender = Provider(named);
        var arrival = ReceiveAsync(contender, message);
        try
        {
            await WaitAsync(async () =>
            {
                await using var observer = new NpgsqlConnection(connection);
                await observer.OpenAsync(Token);
                await using var command = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'intake-delete-gap' AND wait_event = 'advisory')",
                    observer
                );
                return await command.ExecuteScalarAsync(Token) is true;
            });
            await SqlAsync(connection, "DELETE FROM receiver.inbox WHERE processed_at IS NOT NULL");
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(282001)", gate);
            await release.ExecuteNonQueryAsync(Token);
        }

        Assert.Equal(
            InboxReceiveResult.Queued,
            await arrival.WaitAsync(TimeSpan.FromSeconds(10), Token)
        );
        await using var fresh = Context(connection);
        Assert.Null((await fresh.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
        Assert.Single(await fresh.Set<HandledItem>().ToArrayAsync(Token));
    }
}
