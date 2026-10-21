using System.Collections.Concurrent;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.OutboxDemo.Tests;

public sealed class AdoptionTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PersistedDraftEligibilityAndExpectedVersionControlOutgoingCommands()
    {
        string connection = await DatabaseAsync();
        Guid ready = Guid.NewGuid();
        Guid empty = Guid.NewGuid();
        await using (var setup = ExportDbContext.Create(connection))
        {
            setup.AddRange(ExportRequest.Create(ready, 3), ExportRequest.Create(empty, 0));
            await setup.SaveChangesAsync(Token);
        }
        await using (var writer = ExportDbContext.Create(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            var commands = Commands(writer);
            Assert.Equal(
                ExportSubmissionResult.Ineligible,
                await commands.SubmitAsync(empty, 1, Token)
            );
            Assert.Equal(
                ExportSubmissionResult.Conflict,
                await commands.SubmitAsync(ready, 7, Token)
            );
            Assert.Equal(
                ExportSubmissionResult.Accepted,
                await commands.SubmitAsync(ready, 1, Token)
            );
            await writer.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using (var writer = ExportDbContext.Create(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                ExportSubmissionResult.Ineligible,
                await Commands(writer).SubmitAsync(ready, 2, Token)
            );
            await writer.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using var read = ExportDbContext.Create(connection);
        Assert.False(
            (await read.Set<ExportRequest>().SingleAsync(row => row.Id == empty, Token)).Submitted
        );
        Assert.True(
            (await read.Set<ExportRequest>().SingleAsync(row => row.Id == ready, Token)).Submitted
        );
        var outgoing = Assert.Single(await read.Set<OutboxMessageRecord>().ToArrayAsync(Token));
        Assert.Null(outgoing.TenantKey);
        Assert.Equal(ready, outgoing.Payload.GetProperty("ExportRequestId").GetGuid());
        Assert.Equal(3, outgoing.Payload.GetProperty("Pages").GetInt32());
        Assert.False(read.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task CompetingSubmissionsCommitOneStateChangeAndOneMessage()
    {
        string connection = await DatabaseAsync();
        Guid id = await DraftAsync(connection);
        await using var winner = ExportDbContext.Create(connection);
        await using var loser = ExportDbContext.Create(connection);
        await using var first = await winner.Database.BeginTransactionAsync(Token);
        await using var second = await loser.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            ExportSubmissionResult.Accepted,
            await Commands(winner).SubmitAsync(id, 1, Token)
        );
        Assert.Equal(
            ExportSubmissionResult.Accepted,
            await Commands(loser).SubmitAsync(id, 1, Token)
        );
        await winner.SaveChangesAsync(Token);
        await first.CommitAsync(Token);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => loser.SaveChangesAsync(Token));
        await second.RollbackAsync(Token);
        await using var fresh = ExportDbContext.Create(connection);
        Assert.Equal(2, (await fresh.Set<ExportRequest>().SingleAsync(Token)).Version);
        Assert.Single(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }

    [Fact]
    public async Task RollbackLeavesTheDraftAndFreshContextCanSubmitIt()
    {
        string connection = await DatabaseAsync();
        Guid id = await DraftAsync(connection);
        await using (var writer = ExportDbContext.Create(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                ExportSubmissionResult.Accepted,
                await Commands(writer).SubmitAsync(id, 1, Token)
            );
            await writer.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }
        await using (var fresh = ExportDbContext.Create(connection))
        {
            Assert.False((await fresh.Set<ExportRequest>().SingleAsync(Token)).Submitted);
            Assert.Empty(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token));
            await using var transaction = await fresh.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                ExportSubmissionResult.Accepted,
                await Commands(fresh).SubmitAsync(id, 1, Token)
            );
            await fresh.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
    }

    [Fact]
    public async Task RealHttpAcceptanceThenCompletionFailureRepeatsTheSameDirectedCommand()
    {
        string connection = await DatabaseAsync();
        Guid id = await DraftAsync(connection);
        await SubmitAsync(connection, id);
        await SqlAsync(
            connection,
            "ALTER TABLE exports.outgoing_work ADD CONSTRAINT test_completion CHECK (dispatched_at IS NULL)"
        );
        await using var receiver = await HttpReceiver.StartAsync(Token);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        await using (var context = ExportDbContext.Create(connection))
            await Assert.ThrowsAsync<OutboxCompletionException>(() =>
                Dispatcher(context, client, receiver.Endpoint).DispatchNextAsync(Token)
            );
        await SqlAsync(
            connection,
            "ALTER TABLE exports.outgoing_work DROP CONSTRAINT test_completion; UPDATE exports.outgoing_work SET lease_until = clock_timestamp() - interval '1 second'"
        );
        await using var recovered = ExportDbContext.Create(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(recovered, client, receiver.Endpoint).DispatchNextAsync(Token)
        );
        var delivered = receiver.Messages.ToArray();
        Assert.Equal(2, delivered.Length);
        Assert.Equal(delivered[0].MessageId, delivered[1].MessageId);
        Assert.True(JsonElement.DeepEquals(delivered[0].Payload, delivered[1].Payload));
        Assert.All(
            delivered,
            message => Assert.Equal(id, message.Payload.GetProperty("ExportRequestId").GetGuid())
        );
    }

    [Fact]
    public async Task ExecutableJourneyUsesNativeMigrationAndAnActualHttpReceiver()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var receiver = await HttpReceiver.StartAsync(Token);
        using var output = new StringWriter();
        await DemoJourneys.RunAsync(connection, receiver.Endpoint, output, Token);
        Assert.Contains("one dispatch=Published", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(3, Assert.Single(receiver.Messages).Payload.GetProperty("Pages").GetInt32());
    }

    private async Task<string> DatabaseAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var context = ExportDbContext.Create(connection);
        await context.Database.MigrateAsync(Token);
        return connection;
    }

    private static ExportRequestCommands Commands(ExportDbContext database) =>
        new(database, new EfOutbox<ExportDbContext>(database));

    private static PostgresOutboxDispatcher<ExportDbContext> Dispatcher(
        ExportDbContext database,
        HttpClient client,
        Uri endpoint
    ) =>
        new(
            database,
            new HttpCommandPublisher(client, endpoint),
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );

    private static async Task<Guid> DraftAsync(string connection)
    {
        await using var context = ExportDbContext.Create(connection);
        Guid id = Guid.NewGuid();
        context.Add(ExportRequest.Create(id, 3));
        await context.SaveChangesAsync(Token);
        return id;
    }

    private static async Task SubmitAsync(string connection, Guid id)
    {
        await using var writer = ExportDbContext.Create(connection);
        await using var transaction = await writer.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            ExportSubmissionResult.Accepted,
            await Commands(writer).SubmitAsync(id, 1, Token)
        );
        await writer.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static async Task SqlAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }
}

internal sealed class HttpReceiver(WebApplication application, Uri endpoint) : IAsyncDisposable
{
    internal Uri Endpoint { get; } = endpoint;
    internal ConcurrentQueue<(Guid MessageId, JsonElement Payload)> Messages { get; } = new();
    internal int StatusCode { get; set; } = 202;
    internal bool LoseResponse { get; set; }

    internal static async Task<HttpReceiver> StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var application = builder.Build();
        HttpReceiver? receiver = null;
        application.MapPost(
            "/commands",
            async (Microsoft.AspNetCore.Http.HttpContext context) =>
            {
                using var payload = await JsonDocument.ParseAsync(
                    context.Request.Body,
                    cancellationToken: context.RequestAborted
                );
                Guid id = Guid.Parse(context.Request.Headers["X-Message-Id"].ToString());
                receiver!.Messages.Enqueue((id, payload.RootElement.Clone()));
                if (receiver.LoseResponse)
                    context.Abort();
                else
                    context.Response.StatusCode = receiver.StatusCode;
            }
        );
        await application.StartAsync(cancellationToken);
        var address = application
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        receiver = new(application, new Uri(address + "/commands"));
        return receiver;
    }

    public async ValueTask DisposeAsync() => await application.DisposeAsync();
}
