using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Messaging.Rendering;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Messaging.Tests.Processes;

public sealed class InboxRecoveryProcessTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task AccountingSqlFaultStopsProcessAndRepairAllowsRestartWithTheSameDelivery()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(postgres, rabbit, token);
        await topology.SetupAsync(token);
        Guid messageId = Guid.NewGuid();
        await RetainAsync(topology.Rendering, messageId, Guid.NewGuid(), token);
        var original = await ReadAsync(topology.Rendering, messageId, token);
        await RetainAsync(topology.Rendering, Guid.NewGuid(), Guid.NewGuid(), token);
        await ExecuteAsync(
            topology.Rendering,
            """
            CREATE SEQUENCE rendering.handler_entries;
            CREATE FUNCTION rendering.count_handler_entries() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM nextval('rendering.handler_entries');
                RETURN NEW;
            END; $$;
            CREATE TRIGGER count_handler_entries BEFORE INSERT ON rendering.jobs FOR EACH ROW
                EXECUTE FUNCTION rendering.count_handler_entries();
            ALTER TABLE rendering.jobs ADD CONSTRAINT renderer_unavailable CHECK (false) NOT VALID;
            ALTER TABLE rendering.incoming_work ADD CONSTRAINT accounting_unavailable CHECK (failure_count = 0);
            """,
            token
        );

        // This fault may stop the host before its normal startup message is printed.
        var first = topology.Worker("process", "proof-accounting-fault");
        await first.WaitForExitAsync(token);
        Assert.Contains(
            "Inbox failure accounting was not confirmed",
            first.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("stopping the worker", first.Output, StringComparison.Ordinal);
        Assert.Contains("StopHost", first.Output, StringComparison.Ordinal);
        Assert.Equal(
            1,
            await WorkerTopology.SqlCountAsync(
                topology.Rendering,
                "SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM rendering.handler_entries",
                token
            )
        );
        await topology.AssertCountsAsync(2, 0, 0, token);
        var unrecorded = await ReadAsync(topology.Rendering, messageId, token);
        Assert.Equal(original.Revision, unrecorded.Revision);
        Assert.Equal(0, unrecorded.FailureCount);
        Assert.Null(unrecorded.LastFailedAt);
        Assert.Null(unrecorded.DeadLetteredAt);

        await ExecuteAsync(
            topology.Rendering,
            """
            ALTER TABLE rendering.jobs DROP CONSTRAINT renderer_unavailable;
            ALTER TABLE rendering.incoming_work DROP CONSTRAINT accounting_unavailable;
            """,
            token
        );
        var restarted = await topology.StartWorkerAsync(
            "process",
            "proof-accounting-repaired",
            token
        );
        await topology.WaitForJobsAsync(2, token);
        await restarted.StopAsync(token);
        var completed = await ReadAsync(topology.Rendering, messageId, token);
        Assert.NotNull(completed.ProcessedAt);
        Assert.Equal(original.Revision + 1, completed.Revision);
        Assert.Equal(0, completed.FailureCount);
        Assert.Equal(0, completed.RedriveCount);
        Assert.Equal(original.SubscriptionKey, completed.SubscriptionKey);
        Assert.Equal(original.ProducerKey, completed.ProducerKey);
        Assert.Equal(original.MessageId, completed.MessageId);
        Assert.Equal(original.ReceivedAt, completed.ReceivedAt);
        Assert.True(JsonElement.DeepEquals(original.Payload, completed.Payload));
        Assert.Equal(
            3,
            await WorkerTopology.SqlCountAsync(
                topology.Rendering,
                "SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM rendering.handler_entries",
                token
            )
        );
    }

    [Fact]
    public async Task DurableBudgetSurvivesKilledProcessorAndQuarantineSurvivesAnotherProcessRestart()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(postgres, rabbit, token);
        await topology.SetupAsync(token);
        Guid messageId = Guid.NewGuid(),
            exportId = Guid.NewGuid();
        await RetainAsync(topology.Rendering, messageId, exportId, token);
        await using var barrier = new NpgsqlConnection(topology.Rendering);
        await barrier.OpenAsync(token);
        await using (
            var install = new NpgsqlCommand(
                """
                SELECT pg_advisory_lock(180022);
                CREATE FUNCTION rendering.transient_render_failure() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."ExportRequestId" = CAST(TG_ARGV[0] AS uuid) THEN
                        IF (SELECT failure_count FROM rendering.incoming_work WHERE message_id = CAST(TG_ARGV[1] AS uuid)) > 0 THEN
                            PERFORM pg_advisory_xact_lock(180022);
                        END IF;
                        RAISE EXCEPTION 'transient rendering conflict' USING ERRCODE = '40001';
                    END IF;
                    RETURN NEW;
                END; $$;
                """,
                barrier
            )
        )
            await install.ExecuteNonQueryAsync(token);

        // Only fixture-generated GUIDs are interpolated into this private database trigger.
        await ExecuteAsync(
            topology.Rendering,
            $"""
            CREATE TRIGGER transient_render_failure BEFORE INSERT ON rendering.jobs FOR EACH ROW
                EXECUTE FUNCTION rendering.transient_render_failure('{exportId:D}', '{messageId:D}');
            """,
            token
        );
        var first = await topology.StartWorkerAsync("process", "proof-recovery-first", token);
        try
        {
            await WorkerTopology.EventuallyAsync(
                async () =>
                    await WorkerTopology.SqlCountAsync(
                        topology.Rendering,
                        """
                        SELECT count(*) FROM pg_locks l JOIN pg_stat_activity a ON a.pid = l.pid
                        WHERE a.datname = current_database() AND a.application_name = 'proof-recovery-first'
                            AND l.locktype = 'advisory' AND NOT l.granted
                        """,
                        token
                    ) == 1,
                token
            );
        }
        catch (OperationCanceledException)
        {
            var observed = await ReadAsync(topology.Rendering, messageId, CancellationToken.None);
            Assert.Fail(
                $"Processor did not reach the second-attempt barrier. Count={observed.FailureCount}; Code={observed.LastFailureCode}. {first.Output}"
            );
        }
        Assert.Equal(1, (await ReadAsync(topology.Rendering, messageId, token)).FailureCount);
        // The second attempt is executing SQL, but its abrupt death must not invent a failure count.
        await first.KillAsync(token);
        // Let PostgreSQL leave the blocked statement so it can observe the dead client socket.
        await using (var release = new NpgsqlCommand("SELECT pg_advisory_unlock(180022)", barrier))
            await release.ExecuteNonQueryAsync(token);

        await WorkerTopology.WaitForBackendExitAsync(
            topology.Rendering,
            "proof-recovery-first",
            token
        );
        Assert.Equal(1, (await ReadAsync(topology.Rendering, messageId, token)).FailureCount);
        await topology.AssertCountsAsync(1, 0, 0, token);

        var second = await topology.StartWorkerAsync("process", "proof-recovery-second", token);
        await WorkerTopology.EventuallyAsync(
            async () =>
                (await ReadAsync(topology.Rendering, messageId, token)).DeadLetteredAt is not null,
            token
        );
        await second.StopAsync(token);
        var stopped = await ReadAsync(topology.Rendering, messageId, token);
        Assert.Equal(3, stopped.FailureCount);
        Assert.Equal(3, stopped.FailuresSinceRedrive);
        Assert.Equal("render-retry-limit", stopped.LastFailureCode);
        Assert.Equal(0, stopped.RedriveCount);

        // A third real process can finish unrelated work while the poison row remains stopped.
        await RetainAsync(topology.Rendering, Guid.NewGuid(), Guid.NewGuid(), token);
        var third = await topology.StartWorkerAsync("process", "proof-recovery-third", token);
        await topology.WaitForJobsAsync(1, token);
        await third.StopAsync(token);
        var retained = await ReadAsync(topology.Rendering, messageId, token);
        Assert.Equal(stopped.Revision, retained.Revision);
        Assert.Equal(stopped.DeadLetteredAt, retained.DeadLetteredAt);
        Assert.Equal(3, retained.FailureCount);
        await ExecuteAsync(
            topology.Rendering,
            "DROP TRIGGER transient_render_failure ON rendering.jobs",
            token
        );
        await topology.RetryRenderingAsync(token);
        var fourth = await topology.StartWorkerAsync("process", "proof-recovery-redriven", token);
        await topology.WaitForJobsAsync(2, token);
        await fourth.StopAsync(token);
        var completed = await ReadAsync(topology.Rendering, messageId, token);
        Assert.NotNull(completed.ProcessedAt);
        Assert.Equal(3, completed.FailureCount);
        Assert.Equal(0, completed.FailuresSinceRedrive);
        Assert.Equal(1, completed.RedriveCount);
        Assert.Equal(stopped.ReceivedAt, completed.ReceivedAt);
        Assert.True(JsonElement.DeepEquals(stopped.Payload, completed.Payload));
    }

    private static async Task RetainAsync(
        string connection,
        Guid messageId,
        Guid exportId,
        CancellationToken token
    )
    {
        var services = new ServiceCollection();
        InboxTestHost.Register(services, connection);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RenderDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await scope
            .ServiceProvider.GetRequiredService<IInbox<RenderDbContext>>()
            .ReceiveAsync(
                RenderExportHandler.Subscription,
                new IncomingMessage(
                    messageId,
                    "exports",
                    "exports.render",
                    1,
                    JsonSerializer.SerializeToElement(new { ExportRequestId = exportId, Pages = 2 })
                ),
                token
            );
        await transaction.CommitAsync(token);
    }

    private static async Task<InboxMessageRecord> ReadAsync(
        string connection,
        Guid id,
        CancellationToken token
    )
    {
        await using var database = RenderDbContext.Create(connection);
        return await database
            .Set<InboxMessageRecord>()
            .SingleAsync(row => row.MessageId == id, token);
    }

    private static async Task ExecuteAsync(string connection, string sql, CancellationToken token)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(token);
    }
}
