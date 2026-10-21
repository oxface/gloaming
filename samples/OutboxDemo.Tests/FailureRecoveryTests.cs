using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.OutboxDemo.Tests;

public sealed class FailureRecoveryTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string OperatorToken = "outbox-proof-operator-credential-0123456789";
    private const string Operations = "/operations/outbox/dead-letters";

    [Fact]
    public async Task RealHttpBudgetAndAuthorizedInspectionRetryPreserveTheSubmittedCommand()
    {
        string connection = await DatabaseAsync();
        Guid messageId = await QueueAsync(connection);
        var original = await ReadAsync(connection);
        await using var receiver = await HttpReceiver.StartAsync(Token);
        receiver.StatusCode = 503;
        using var sender = new HttpClient();
        for (int failure = 1; failure <= 3; failure++)
        {
            await using var database = ExportDbContext.Create(connection);
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                Dispatch(database, sender, receiver.Endpoint).DispatchNextAsync(Token)
            );
            var row = await ReadAsync(connection);
            Assert.Equal(failure, row.FailureCount);
            Assert.Equal(failure, row.FailuresSinceRedrive);
            Assert.Equal(failure == 3, row.DeadLetteredAt is not null);
            if (failure < 3)
                await SqlAsync(
                    connection,
                    "UPDATE exports.outgoing_work SET available_at = clock_timestamp()"
                );
        }

        await using var app = OutboxDemoHost.Build(
            connection,
            "http://127.0.0.1:0",
            operatorToken: OperatorToken
        );
        await app.StartAsync(Token);
        using var operatorClient = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await operatorClient.GetAsync(Operations, Token)).StatusCode
        );
        operatorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            OperatorToken
        );
        var page = (
            await operatorClient.GetFromJsonAsync<OutboxDeadLetterPage>(
                Operations + "?pageSize=1&routeKey=ignored",
                Token
            )
        )!;
        var item = Assert.Single(page.Items);
        Assert.Equal(messageId, item.MessageId);
        Assert.Equal("export-retry-limit", item.LastFailureCode);
        string body = await operatorClient.GetStringAsync(Operations + "/" + messageId, Token);
        foreach (
            string excluded in new[]
            {
                "payload",
                "tenantKey",
                "traceParent",
                "leaseToken",
                "exception",
            }
        )
            Assert.DoesNotContain(excluded, body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await operatorClient.GetAsync(Operations + "?pageSize=101", Token)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await operatorClient.GetAsync(Operations + "?after=invalid", Token)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await operatorClient.PostAsJsonAsync(
                    Operations + "/" + messageId + "/retry",
                    new { expectedRevision = item.Revision },
                    Token
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await operatorClient.PostAsJsonAsync(
                    Operations + "/" + messageId + "/retry",
                    new { expectedRevision = item.Revision },
                    Token
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await operatorClient.GetAsync(Operations + "/" + messageId, Token)).StatusCode
        );
        receiver.StatusCode = 202;
        await using (var fresh = ExportDbContext.Create(connection))
            Assert.Equal(
                OutboxDispatchResult.Published,
                await Dispatch(fresh, sender, receiver.Endpoint).DispatchNextAsync(Token)
            );
        var completed = await ReadAsync(connection);
        Assert.Equal(4, completed.Attempts);
        Assert.Equal(3, completed.FailureCount);
        Assert.Equal(0, completed.FailuresSinceRedrive);
        Assert.Equal(1, completed.RedriveCount);
        Assert.Equal(original.QueuedAt, completed.QueuedAt);
        Assert.True(JsonElement.DeepEquals(original.Payload, completed.Payload));
        Assert.All(receiver.Messages, received => Assert.Equal(messageId, received.MessageId));
    }

    [Fact]
    public async Task AcceptanceBeforeCompletionFailureDoesNotInvokePublicationPolicyOrCountFailure()
    {
        string connection = await DatabaseAsync();
        Guid message = await QueueAsync(connection);
        await SqlAsync(
            connection,
            "ALTER TABLE exports.outgoing_work ADD CONSTRAINT reject_completion CHECK (dispatched_at IS NULL)"
        );
        await using var receiver = await HttpReceiver.StartAsync(Token);
        using var sender = new HttpClient();
        var policy = new ObservedPolicy();
        await using (var database = ExportDbContext.Create(connection))
        {
            var failure = await Assert.ThrowsAsync<OutboxCompletionException>(() =>
                new PostgresOutboxDispatcher<ExportDbContext>(
                    database,
                    new HttpCommandPublisher(sender, receiver.Endpoint),
                    new(TimeSpan.FromSeconds(30), TimeSpan.Zero),
                    failurePolicy: policy
                ).DispatchNextAsync(Token)
            );
            Assert.Equal(message, failure.MessageId);
            Assert.IsType<PostgresException>(failure.InnerException);
        }

        var ambiguous = await ReadAsync(connection);
        Assert.Equal(0, policy.Calls);
        Assert.Equal(0, ambiguous.FailureCount);
        Assert.NotNull(ambiguous.LeaseToken);
        await SqlAsync(
            connection,
            "ALTER TABLE exports.outgoing_work DROP CONSTRAINT reject_completion; UPDATE exports.outgoing_work SET lease_until = clock_timestamp() - interval '1 second'"
        );
        await using var successor = ExportDbContext.Create(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatch(successor, sender, receiver.Endpoint).DispatchNextAsync(Token)
        );
        Assert.Equal(2, receiver.Messages.Count);
        Assert.All(receiver.Messages, item => Assert.Equal(message, item.MessageId));
        Assert.True(
            JsonElement.DeepEquals(
                receiver.Messages.First().Payload,
                receiver.Messages.Last().Payload
            )
        );
        Assert.Equal(0, (await ReadAsync(connection)).FailureCount);
    }

    [Fact]
    public async Task LostHttpResponseCountsAnObservationWithoutAssertingRemoteRejection()
    {
        string connection = await DatabaseAsync();
        Guid message = await QueueAsync(connection);
        await using var receiver = await HttpReceiver.StartAsync(Token);
        receiver.LoseResponse = true;
        using var sender = new HttpClient();
        await using (var database = ExportDbContext.Create(connection))
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                Dispatch(database, sender, receiver.Endpoint).DispatchNextAsync(Token)
            );
        Assert.Equal(message, Assert.Single(receiver.Messages).MessageId);
        var row = await ReadAsync(connection);
        Assert.Equal(1, row.FailureCount);
        Assert.Equal("export-transport-transient", row.LastFailureCode);
        Assert.Null(row.LeaseToken);
        Assert.Null(row.DispatchedAt);
    }

    [Fact]
    public async Task MissingOrWrongCredentialsRejectBeforeUnavailableDatabaseAndUnconfiguredRoutesAreAbsent()
    {
        const string unavailable = "Host=127.0.0.1;Port=1;Database=unused;Username=unused";
        await using var app = OutboxDemoHost.Build(
            unavailable,
            "http://127.0.0.1:0",
            operatorToken: OperatorToken
        );
        await app.StartAsync(Token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        foreach (string? credential in new[] { null, "wrong-credential" })
        {
            client.DefaultRequestHeaders.Authorization = credential is null
                ? null
                : new AuthenticationHeaderValue("Bearer", credential);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync(Operations, Token)).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync(Operations + "/" + Guid.NewGuid(), Token)).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (
                    await client.PostAsJsonAsync(
                        Operations + "/" + Guid.NewGuid() + "/retry",
                        new { expectedRevision = 0 },
                        Token
                    )
                ).StatusCode
            );
        }

        await using var absent = OutboxDemoHost.Build(unavailable, "http://127.0.0.1:0");
        await absent.StartAsync(Token);
        using var unconfigured = new HttpClient { BaseAddress = new Uri(absent.Urls.Single()) };
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await unconfigured.GetAsync(Operations, Token)).StatusCode
        );
        Assert.Throws<ArgumentException>(() =>
            OutboxDemoHost.Build(unavailable, "http://127.0.0.1:0", operatorToken: "short")
        );
    }

    [Fact]
    public async Task OperatorRetryCannotBypassCurrentPublisherContractValidation()
    {
        string connection = await DatabaseAsync();
        var message = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "exports.render",
            "exports.render",
            2,
            new { Pages = 3 },
            new JsonSerializerOptions()
        );
        await using (var producer = ExportDbContext.Create(connection))
        {
            await using var transaction = await producer.Database.BeginTransactionAsync(Token);
            new EfOutbox<ExportDbContext>(producer).Enqueue(message);
            await producer.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        using var sender = new HttpClient();
        for (int cycle = 0; cycle < 2; cycle++)
        {
            await using (var database = ExportDbContext.Create(connection))
                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    Dispatch(database, sender, new Uri("http://127.0.0.1:1/unused"))
                        .DispatchNextAsync(Token)
                );
            var stopped = await ReadAsync(connection);
            Assert.Equal("export-contract-unsupported", stopped.LastFailureCode);
            Assert.Equal(cycle + 1, stopped.FailureCount);
            Assert.Equal(2, stopped.SchemaVersion);
            if (cycle == 0)
            {
                var services = new ServiceCollection();
                services.AddDbContext<ExportDbContext>(options =>
                    ExportDbContext.Configure(options, connection)
                );
                services.AddPostgresOutboxDeadLetters<ExportDbContext>();
                await using var provider = services.BuildServiceProvider();
                await using var scope = provider.CreateAsyncScope();
                Assert.Equal(
                    OutboxRetryResult.Retried,
                    await scope
                        .ServiceProvider.GetRequiredService<IOutboxDeadLetters<ExportDbContext>>()
                        .RetryAsync("exports.render", message.MessageId, stopped.Revision, Token)
                );
            }
        }

        Assert.True(JsonElement.DeepEquals(message.Payload, (await ReadAsync(connection)).Payload));
    }

    private async Task<string> DatabaseAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = ExportDbContext.Create(connection);
        await database.Database.MigrateAsync(Token);
        return connection;
    }

    private static async Task<Guid> QueueAsync(string connection)
    {
        await using var database = ExportDbContext.Create(connection);
        Guid id = Guid.NewGuid();
        database.Add(ExportRequest.Create(id, 3));
        await database.SaveChangesAsync(Token);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            ExportSubmissionResult.Accepted,
            await new ExportRequestCommands(
                database,
                new EfOutbox<ExportDbContext>(database)
            ).SubmitAsync(id, 1, Token)
        );
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return (await database.Set<OutboxMessageRecord>().SingleAsync(Token)).MessageId;
    }

    private static PostgresOutboxDispatcher<ExportDbContext> Dispatch(
        ExportDbContext database,
        HttpClient client,
        Uri receiver
    ) =>
        new(
            database,
            new HttpCommandPublisher(client, receiver),
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero),
            failurePolicy: new ExportOutboxFailurePolicy()
        );

    private static async Task<OutboxMessageRecord> ReadAsync(string connection)
    {
        await using var database = ExportDbContext.Create(connection);
        return await database.Set<OutboxMessageRecord>().AsNoTracking().SingleAsync(Token);
    }

    private static async Task SqlAsync(string connection, string sql)
    {
        await using var database = ExportDbContext.Create(connection);
        await database.Database.ExecuteSqlRawAsync(sql, Token);
    }

    private sealed class ObservedPolicy : IOutboxFailurePolicy<ExportDbContext>
    {
        internal int Calls { get; private set; }

        public OutboxFailureDecision Decide(OutboxFailure failure)
        {
            Calls++;
            return new OutboxFailureDecision.DeadLetter("unexpected");
        }
    }
}
