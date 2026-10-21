using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.OutboxDemo;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.MessagingWorkerDemo.Tests;

public sealed class OutboxRecoveryProcessTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string OperatorToken = "process-outbox-operator-proof-0123456789";

    [Fact(Timeout = 90_000)]
    public async Task KillDuringAccountingPreservesBudgetAndRestartedDeadLetterNeedsOperatorRetry()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await DatabaseAsync();
        var message = await EnqueueAsync(connection);
        await using var receiver = await Receiver.StartAsync();
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync(token);
        await using (
            var command = new NpgsqlCommand(
                """
                SELECT pg_advisory_lock(280022);
                CREATE FUNCTION exports.hold_second_failure() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.failure_count > OLD.failure_count AND OLD.failure_count > 0 THEN
                        PERFORM pg_advisory_xact_lock(280022);
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER hold_second_failure BEFORE UPDATE ON exports.outgoing_work FOR EACH ROW
                    EXECUTE FUNCTION exports.hold_second_failure();
                """,
                barrier
            )
        )
            await command.ExecuteNonQueryAsync(Token);

        await using (var first = Worker(connection, receiver.Endpoint, "budget-before-death"))
        {
            await WaitAsync(async () =>
                await ScalarAsync(
                    connection,
                    "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'budget-before-death' AND wait_event = 'advisory')"
                )
            );
            var inFlight = await ReadAsync(connection, message.MessageId);
            Assert.Equal(1, inFlight.FailureCount);
            Assert.Equal(2, inFlight.Attempts);
            await first.KillAsync(Token);
        }

        await using (
            var release = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(280022); DROP TRIGGER hold_second_failure ON exports.outgoing_work",
                barrier
            )
        )
            await release.ExecuteNonQueryAsync(Token);
        var killed = await ReadAsync(connection, message.MessageId);
        Assert.Equal(1, killed.FailureCount);
        Assert.Equal(1, killed.FailuresSinceRedrive);
        Assert.NotNull(killed.LeaseToken);
        await using (var successor = Worker(connection, receiver.Endpoint, "budget-after-death"))
        {
            await WaitAsync(async () =>
                (await ReadAsync(connection, message.MessageId)).DeadLetteredAt is not null
            );
            await successor.StopAsync(Token);
        }

        var stopped = await ReadAsync(connection, message.MessageId);
        Assert.Equal(3, stopped.FailureCount);
        Assert.Equal(4, stopped.Attempts);
        Assert.Equal(4, receiver.Messages.Count(row => row.MessageId == message.MessageId));
        receiver.StatusCode = 202;
        await using var restarted = Worker(
            connection,
            receiver.Endpoint,
            "dead-letter-restarted",
            operatorToken: OperatorToken
        );
        string address = await restarted.WaitForOutputAsync("Now listening on:", Token);
        using var client = new HttpClient
        {
            BaseAddress = new Uri(
                address[(address.IndexOf("http", StringComparison.Ordinal))..].Trim()
            ),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            OperatorToken
        );
        var unrelated = await EnqueueAsync(connection);
        await WaitAsync(async () =>
            (await ReadAsync(connection, unrelated.MessageId)).DispatchedAt is not null
        );
        var stillStopped = await ReadAsync(connection, message.MessageId);
        Assert.Equal(stopped.Revision, stillStopped.Revision);
        Assert.Equal(stopped.Attempts, stillStopped.Attempts);
        Assert.Equal(4, receiver.Messages.Count(row => row.MessageId == message.MessageId));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await client.PostAsJsonAsync(
                    "/operations/outbox/dead-letters/" + message.MessageId + "/retry",
                    new { expectedRevision = stopped.Revision },
                    Token
                )
            ).StatusCode
        );
        await WaitAsync(async () =>
            (await ReadAsync(connection, message.MessageId)).DispatchedAt is not null
        );
        await restarted.StopAsync(Token);
        var completed = await ReadAsync(connection, message.MessageId);
        Assert.Equal(3, completed.FailureCount);
        Assert.Equal(0, completed.FailuresSinceRedrive);
        Assert.Equal(1, completed.RedriveCount);
        Assert.Equal(5, completed.Attempts);
        Assert.True(JsonElement.DeepEquals(message.Payload, completed.Payload));
    }

    [Fact(Timeout = 90_000)]
    public async Task AccountingSqlFaultStopsTheRealHostAndRepairAllowsSameMessageAfterRestart()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await DatabaseAsync();
        var original = await EnqueueAsync(connection);
        await SqlAsync(
            connection,
            "ALTER TABLE exports.outgoing_work ADD CONSTRAINT accounting_unavailable CHECK (failure_count = 0)"
        );
        await using var receiver = await Receiver.StartAsync();
        await using (var broken = Worker(connection, receiver.Endpoint, "accounting-broken"))
        {
            await broken.WaitForExitAsync(token);
            Assert.Contains(
                "Outbox failure accounting was not confirmed",
                broken.Output,
                StringComparison.Ordinal
            );
            Assert.Contains("stopping the worker", broken.Output, StringComparison.Ordinal);
            Assert.Contains("StopHost", broken.Output, StringComparison.Ordinal);
        }

        Assert.Single(receiver.Messages);
        var unrecorded = await ReadAsync(connection, original.MessageId);
        Assert.Equal(1, unrecorded.Attempts);
        Assert.Equal(0, unrecorded.FailureCount);
        Assert.Null(unrecorded.LastFailureCode);
        Assert.NotNull(unrecorded.LeaseToken);
        await SqlAsync(
            connection,
            "ALTER TABLE exports.outgoing_work DROP CONSTRAINT accounting_unavailable"
        );
        receiver.StatusCode = 202;
        await using var repaired = Worker(connection, receiver.Endpoint, "accounting-repaired");
        await WaitAsync(async () =>
            (await ReadAsync(connection, original.MessageId)).DispatchedAt is not null
        );
        await repaired.StopAsync(Token);
        var completed = await ReadAsync(connection, original.MessageId);
        Assert.Equal(2, completed.Attempts);
        Assert.Equal(0, completed.FailureCount);
        Assert.True(JsonElement.DeepEquals(original.Payload, completed.Payload));
        Assert.All(
            receiver.Messages,
            received => Assert.Equal(original.MessageId, received.MessageId)
        );
    }

    private static ChildHost Worker(
        string connection,
        Uri receiver,
        string name,
        string? operatorToken = null
    )
    {
        var environment = new Dictionary<string, string>
        {
            ["OUTBOX_DEMO_CONNECTION_STRING"] = new NpgsqlConnectionStringBuilder(connection)
            {
                ApplicationName = name,
            }.ConnectionString,
            ["OUTBOX_DEMO_RECEIVER"] = receiver.ToString(),
            ["OUTBOX_DEMO_URL"] = "http://127.0.0.1:0",
            ["OUTBOX_DEMO_LEASE_SECONDS"] = "2",
        };
        if (operatorToken is not null)
            environment["OUTBOX_DEMO_OPERATOR_TOKEN"] = operatorToken;

        return new(typeof(DemoJourneys).Assembly, environment, "--serve", "--worker");
    }

    private async Task<string> DatabaseAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = ExportDbContext.Create(connection);
        await database.Database.MigrateAsync(Token);
        return connection;
    }

    private static async Task<OutgoingMessage> EnqueueAsync(string connection)
    {
        var message = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "exports.render",
            "exports.render",
            1,
            new { ExportRequestId = Guid.NewGuid(), Pages = 3 },
            new JsonSerializerOptions()
        );
        await using var database = ExportDbContext.Create(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        new EfOutbox<ExportDbContext>(database).Enqueue(message);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return message;
    }

    private static async Task<OutboxMessageRecord> ReadAsync(string connection, Guid id)
    {
        await using var database = ExportDbContext.Create(connection);
        return await database
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(row => row.MessageId == id, Token);
    }

    private static async Task SqlAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<bool> ScalarAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        return await command.ExecuteScalarAsync(Token) is true;
    }

    private static async Task WaitAsync(Func<Task<bool>> observedCondition)
    {
        while (!await observedCondition())
            await Task.Delay(20, Token);
    }

    private sealed class Receiver(WebApplication app, Uri endpoint) : IAsyncDisposable
    {
        private int statusCode = 503;
        internal int StatusCode
        {
            get => Volatile.Read(ref statusCode);
            set => Volatile.Write(ref statusCode, value);
        }
        internal Uri Endpoint => endpoint;
        internal ConcurrentQueue<(Guid MessageId, JsonElement Payload)> Messages { get; } = new();

        internal static async Task<Receiver> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            Receiver? receiver = null;
            app.MapPost(
                "/commands",
                async (HttpContext context) =>
                {
                    using var payload = await JsonDocument.ParseAsync(
                        context.Request.Body,
                        cancellationToken: context.RequestAborted
                    );
                    receiver!.Messages.Enqueue(
                        (
                            Guid.Parse(context.Request.Headers["X-Message-Id"].ToString()),
                            payload.RootElement.Clone()
                        )
                    );
                    context.Response.StatusCode = receiver.StatusCode;
                }
            );
            await app.StartAsync(Token);
            receiver = new(app, new Uri(app.Urls.Single() + "/commands"));
            return receiver;
        }

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
}
