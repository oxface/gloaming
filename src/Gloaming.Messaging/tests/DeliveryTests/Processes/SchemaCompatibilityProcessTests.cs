using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Messaging.Exports;
using Gloaming.Samples.Messaging.Rendering;
using Gloaming.Samples.Messaging.Worker.Transport;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Gloaming.Samples.Messaging.Tests.Processes;

public sealed class SchemaCompatibilityProcessTests(
    PostgreSqlFixture postgres,
    RabbitMqFixture rabbit
) : IClassFixture<PostgreSqlFixture>, IClassFixture<RabbitMqFixture>
{
    // Independent historical JSON, not produced by today's DTO or decoder.
    private const string OldOutgoing = """
        {"ExportRequestId":"2c355b7b-e2ee-4985-ad51-fb3dd52bdc49","Pages":3}
        """;
    private const string OldIncoming = """
        {"ExportRequestId":"e8fbb9df-68af-4178-9e42-f4487fdd79de","Pages":4}
        """;

    [Fact]
    public async Task FiniteCompatibilityCommandKeepsBothOriginalWireSchemas()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        string exports = await postgres.CreateDatabaseAsync(token);
        string rendering = await postgres.CreateDatabaseAsync(token);
        var environment = new Dictionary<string, string>
        {
            ["MESSAGING_DEMO_SENDER_CONNECTION_STRING"] = exports,
            ["MESSAGING_DEMO_RECEIVER_CONNECTION_STRING"] = rendering,
            ["MESSAGING_DEMO_RABBITMQ"] = rabbit.ConnectionString,
        };
        await using var child = new ChildHost(
            typeof(MessagingJourney).Assembly,
            environment,
            "--schema-compatibility"
        );
        Assert.True(await child.WaitForExitAsync(token) == 0, child.Output);
        Assert.Contains("schema 1: delivery", child.Output, StringComparison.Ordinal);
        Assert.Contains("schema 2: delivery", child.Output, StringComparison.Ordinal);
        Assert.Contains(
            "retained schemas=1,2; local jobs=2; original envelopes unchanged",
            child.Output,
            StringComparison.Ordinal
        );
        await using var sender = ExportDbContext.Create(exports);
        await using var receiver = RenderDbContext.Create(rendering);
        var messages = await sender
            .Set<OutboxMessageRecord>()
            .OrderBy(row => row.SchemaVersion)
            .ToArrayAsync(token);
        Assert.Equal(2, messages.Length);
        Assert.Equal(1, messages[0].SchemaVersion);
        Assert.Equal(2, messages[1].SchemaVersion);
        Assert.Equal(3, messages[0].Payload.GetProperty("Pages").GetInt32());
        Assert.Equal(5, messages[1].Payload.GetProperty("PageCount").GetInt32());
        Assert.Equal(2, await receiver.Set<RenderJob>().CountAsync(token));
        Assert.Equal(
            2,
            await receiver
                .Set<InboxMessageRecord>()
                .CountAsync(row => row.ProcessedAt != null, token)
        );
        await using var repeated = new ChildHost(
            typeof(MessagingJourney).Assembly,
            environment,
            "--schema-compatibility"
        );
        Assert.NotEqual(0, await repeated.WaitForExitAsync(token));
        Assert.Contains(
            "Use disposable empty Exports storage",
            repeated.Output,
            StringComparison.Ordinal
        );
        Assert.Equal(2, await sender.Set<OutboxMessageRecord>().CountAsync(token));
        Assert.Equal(2, await receiver.Set<RenderJob>().CountAsync(token));
    }

    [Fact]
    public async Task MixedSchemasAndHistoricalIntakeRecoverAfterKilledDispatchAndProcessing()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        var token = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(postgres, rabbit, token);
        await topology.SetupAsync(token);
        Guid oldMessage = Guid.NewGuid(),
            oldReceipt = Guid.NewGuid(),
            newExport = Guid.NewGuid();
        await SeedHistoricalAsync(topology, oldMessage, oldReceipt, token);
        await using (var writer = ExportDbContext.Create(topology.Exports))
        {
            writer.Add(ExportRequest.Create(newExport, 5));
            await writer.SaveChangesAsync(token);
            await using var transaction = await writer.Database.BeginTransactionAsync(token);
            Assert.Equal(
                ExportSubmissionResult.Accepted,
                await new ExportRequestCommands(
                    writer,
                    new EfOutbox<ExportDbContext>(writer),
                    renderSchemaVersion: 2
                ).SubmitAsync(newExport, 1, token)
            );
            await writer.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }

        var receiver = await topology.StartWorkerAsync("receive", "proof-compat-receive", token);
        await using (
            var barrier = await DatabaseBarrier.HoldAsync(
                topology.Exports,
                publicationCompletion: true,
                token
            )
        )
        {
            var original = await topology.StartWorkerAsync(
                "dispatch",
                "proof-compat-old-dispatch",
                token,
                leaseSeconds: 3
            );
            await original.WaitForOutputAsync($"Publication {oldMessage} confirmed", token);
            await barrier.WaitForBlockedAsync(1, token);
            await receiver.WaitForOutputAsync(
                $"Delivery {oldMessage} retained and acknowledged: Queued",
                token
            );
            await original.KillAsync(token);
            await barrier.ReleaseAsync(token);
            await WorkerTopology.WaitForBackendExitAsync(
                topology.Exports,
                "proof-compat-old-dispatch",
                token
            );
        }

        await using (
            var barrier = await DatabaseBarrier.HoldAsync(
                topology.Rendering,
                publicationCompletion: false,
                token
            )
        )
        {
            var original = await topology.StartWorkerAsync(
                "process",
                "proof-compat-old-process",
                token
            );
            await barrier.WaitForBlockedAsync(1, token);
            await original.KillAsync(token);
            await barrier.ReleaseAsync(token);
            await WorkerTopology.WaitForBackendExitAsync(
                topology.Rendering,
                "proof-compat-old-process",
                token
            );
        }

        await topology.AssertCountsAsync(2, 0, 0, token);
        await using (var read = ExportDbContext.Create(topology.Exports))
        {
            var retained = await read.Set<OutboxMessageRecord>()
                .SingleAsync(row => row.MessageId == oldMessage, token);
            Assert.Null(retained.DispatchedAt);
            Assert.NotNull(retained.LeaseToken);
            Assert.Equal(1, retained.Attempts);
            Assert.Equal(0, retained.FailureCount);
        }

        var processor = await topology.StartWorkerAsync(
            "process",
            "proof-compat-new-process",
            token
        );
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE lease_until <= clock_timestamp()",
                    token
                ) == 1,
            token
        );
        var firstDispatcher = await topology.StartWorkerAsync(
            "dispatch",
            "proof-compat-new-dispatch-1",
            token
        );
        var secondDispatcher = await topology.StartWorkerAsync(
            "dispatch",
            "proof-compat-new-dispatch-2",
            token
        );
        await receiver.WaitForOutputAsync(
            $"Delivery {oldMessage} retained and acknowledged: AlreadyReceived",
            token
        );
        await topology.WaitForJobsAsync(3, token);
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE dispatched_at IS NOT NULL",
                    token
                ) == 2,
            token
        );
        await firstDispatcher.StopAsync(token);
        await secondDispatcher.StopAsync(token);
        await receiver.StopAsync(token);
        await processor.StopAsync(token);
        await topology.AssertCountsAsync(3, 3, 3, token);
        await topology.AssertQueueEmptyAsync(token);

        await using var sender = ExportDbContext.Create(topology.Exports);
        await using var rendering = RenderDbContext.Create(topology.Rendering);
        var outgoing = await sender
            .Set<OutboxMessageRecord>()
            .OrderBy(row => row.SchemaVersion)
            .ToArrayAsync(token);
        Assert.Equal(2, outgoing.Length);
        Assert.Equal(oldMessage, outgoing[0].MessageId);
        Assert.Equal(1, outgoing[0].SchemaVersion);
        Assert.Equal(2, outgoing[0].Attempts);
        Assert.Equal(2, outgoing[1].SchemaVersion);
        Assert.Equal(1, outgoing[1].Attempts);
        using var expectedOld = JsonDocument.Parse(OldOutgoing);
        Assert.True(JsonElement.DeepEquals(expectedOld.RootElement, outgoing[0].Payload));
        Assert.Equal(newExport, outgoing[1].Payload.GetProperty("ExportRequestId").GetGuid());
        Assert.Equal(5, outgoing[1].Payload.GetProperty("PageCount").GetInt32());
        foreach (var original in outgoing)
        {
            var received = await rendering
                .Set<InboxMessageRecord>()
                .SingleAsync(row => row.MessageId == original.MessageId, token);
            Assert.Equal(original.SchemaVersion, received.SchemaVersion);
            Assert.Equal(original.MessageName, received.MessageName);
            Assert.Equal(original.CorrelationId, received.CorrelationId);
            Assert.Equal(original.CausationId, received.CausationId);
            Assert.True(JsonElement.DeepEquals(original.Payload, received.Payload));
            Assert.Equal(0, received.FailureCount);
        }

        var historical = await rendering
            .Set<InboxMessageRecord>()
            .SingleAsync(row => row.MessageId == oldReceipt, token);
        Assert.Equal(1, historical.SchemaVersion);
        Assert.Equal("historical-incoming", historical.CorrelationId);
        Assert.Equal("historical-parent", historical.CausationId);
        using var expectedIncoming = JsonDocument.Parse(OldIncoming);
        Assert.True(JsonElement.DeepEquals(expectedIncoming.RootElement, historical.Payload));
        await topology.AssertJobAsync(Guid.Parse("2c355b7b-e2ee-4985-ad51-fb3dd52bdc49"), 3, token);
        await topology.AssertJobAsync(Guid.Parse("e8fbb9df-68af-4178-9e42-f4487fdd79de"), 4, token);
        await topology.AssertJobAsync(newExport, 5, token);
    }

    private static async Task SeedHistoricalAsync(
        WorkerTopology topology,
        Guid message,
        Guid receipt,
        CancellationToken token
    )
    {
        await using (var connection = new NpgsqlConnection(topology.Exports))
        {
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO exports.outgoing_work
                    (message_id, destination, message_name, schema_version, payload, attempts, queued_at, available_at, correlation_id, causation_id)
                VALUES (@id, 'exports.render', 'exports.render', 1, @payload, 0,
                    '2020-01-01T00:00:00Z', '2020-01-01T00:00:00Z', 'historical-outgoing', 'historical-parent')
                """,
                connection
            );
            command.Parameters.AddWithValue("id", message);
            command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, OldOutgoing);
            await command.ExecuteNonQueryAsync(token);
        }

        await using var receiver = new NpgsqlConnection(topology.Rendering);
        await receiver.OpenAsync(token);
        await using var intake = new NpgsqlCommand(
            """
            INSERT INTO rendering.incoming_work
                (subscription_key, producer_key, message_id, message_name, schema_version, payload, received_at, available_at, correlation_id, causation_id)
            VALUES ('rendering.jobs', 'exports', @id, 'exports.render', 1, @payload,
                '2020-01-01T00:00:00Z', '2020-01-01T00:00:00Z', 'historical-incoming', 'historical-parent')
            """,
            receiver
        );
        intake.Parameters.AddWithValue("id", receipt);
        intake.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, OldIncoming);
        await intake.ExecuteNonQueryAsync(token);
    }
}
