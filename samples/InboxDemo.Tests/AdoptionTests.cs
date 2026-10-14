using System.Net;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.OutboxDemo;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.InboxDemo.Tests;

[CollectionDefinition("Rendering PostgreSQL")]
public sealed class RenderingPostgresProofs : ICollectionFixture<PostgreSqlFixture>;

[Collection("Rendering PostgreSQL")]
public sealed class AdoptionTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HttpAcceptanceRetainsWorkBeforeProcessingAndConflictingIdentityIsNotAccepted()
    {
        string connection = await DatabaseAsync();
        await using var app = InboxDemoHost.Build(connection, "http://127.0.0.1:0");
        await app.StartAsync(Token);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var publisher = new HttpCommandPublisher(client, Address(app.Services));
        Guid export = Guid.NewGuid();
        var message = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "exports.render",
            "exports.render",
            1,
            new RenderExportV1(export, 3),
            new JsonSerializerOptions(),
            correlationId: "conversation",
            causationId: "upstream"
        );
        await publisher.PublishAsync(message, Token);
        await publisher.PublishAsync(message, Token);
        await using (var fresh = RenderDbContext.Create(connection))
        {
            Assert.Empty(await fresh.Set<RenderJob>().ToArrayAsync(Token));
            var row = Assert.Single(await fresh.Set<InboxMessageRecord>().ToArrayAsync(Token));
            Assert.Null(row.ProcessedAt);
            Assert.Equal("conversation", row.CorrelationId);
            Assert.Equal("upstream", row.CausationId);
            Assert.Null(row.TenantKey);
            Assert.Null(fresh.Model.FindEntityType(typeof(OutboxMessageRecord)));
            Assert.False(fresh.Database.HasPendingModelChanges());
        }
        var conflict = new OutgoingMessage(
            message.MessageId,
            message.RouteKey,
            message.MessageName,
            1,
            JsonSerializer.SerializeToElement(new RenderExportV1(export, 4)),
            correlationId: "conversation",
            causationId: "upstream"
        );
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
            publisher.PublishAsync(conflict, Token)
        );
        Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(app.Services));
        await publisher.PublishAsync(message, Token);
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(app.Services));
        // A new delivery identity does not change the consumer's semantic job identity.
        await publisher.PublishAsync(
            OutgoingMessage.FromPayload(
                Guid.NewGuid(),
                "exports.render",
                "exports.render",
                1,
                new RenderExportV1(export, 3),
                new JsonSerializerOptions()
            ),
            Token
        );
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(app.Services));
        await using var read = RenderDbContext.Create(connection);
        Assert.Equal(export, (await read.Set<RenderJob>().SingleAsync(Token)).ExportRequestId);
        Assert.Equal(
            2,
            await read.Set<InboxMessageRecord>().CountAsync(row => row.ProcessedAt != null, Token)
        );
        Assert.Null(app.Services.GetService<IMessagePublisher>());
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Null(scope.ServiceProvider.GetService<IOutbox<RenderDbContext>>());
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task IntakeSqlFailureReturnsNoAcceptanceAndFreshRequestCanRecover()
    {
        string connection = await DatabaseAsync();
        await SqlAsync(
            connection,
            "ALTER TABLE rendering.incoming_work ADD CONSTRAINT proof_intake CHECK (schema_version > 1)"
        );
        await using var app = InboxDemoHost.Build(connection, "http://127.0.0.1:0");
        await app.StartAsync(Token);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var publisher = new HttpCommandPublisher(client, Address(app.Services));
        var message = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "exports.render",
            "exports.render",
            1,
            new RenderExportV1(Guid.NewGuid(), 3),
            new JsonSerializerOptions()
        );
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
            publisher.PublishAsync(message, Token)
        );
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        await using (var observer = RenderDbContext.Create(connection))
            Assert.Empty(await observer.Set<InboxMessageRecord>().ToArrayAsync(Token));
        await SqlAsync(
            connection,
            "ALTER TABLE rendering.incoming_work DROP CONSTRAINT proof_intake"
        );
        await publisher.PublishAsync(message, Token);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(app.Services));
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task OptionalWorkerProcessesCommittedIntakeWithoutAnOutboxDependency()
    {
        string connection = await DatabaseAsync();
        await using var app = InboxDemoHost.Build(connection, "http://127.0.0.1:0", worker: true);
        await app.StartAsync(Token);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var publisher = new HttpCommandPublisher(client, Address(app.Services));
        Guid id = Guid.NewGuid();
        await publisher.PublishAsync(
            OutgoingMessage.FromPayload(
                Guid.NewGuid(),
                "exports.render",
                "exports.render",
                1,
                new RenderExportV1(id, 2),
                new JsonSerializerOptions()
            ),
            Token
        );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var read = RenderDbContext.Create(connection);
            if (
                await read.Set<InboxMessageRecord>()
                    .AnyAsync(row => row.ProcessedAt != null, timeout.Token)
            )
            {
                Assert.Equal(
                    id,
                    (await read.Set<RenderJob>().SingleAsync(timeout.Token)).ExportRequestId
                );
                break;
            }
            await Task.Delay(10, timeout.Token);
        }
        await app.StopAsync(Token);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "unadmitted-tenant")]
    public async Task InvalidCommandOrTenantMetadataIsRejectedBeforeRetainingWork(
        int pages,
        string? tenant
    )
    {
        string connection = await DatabaseAsync();
        await using var app = InboxDemoHost.Build(connection, "http://127.0.0.1:0");
        await app.StartAsync(Token);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var publisher = new HttpCommandPublisher(client, Address(app.Services));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
            publisher.PublishAsync(
                OutgoingMessage.FromPayload(
                    Guid.NewGuid(),
                    "exports.render",
                    "exports.render",
                    1,
                    new RenderExportV1(Guid.NewGuid(), pages),
                    new JsonSerializerOptions(),
                    tenant
                ),
                Token
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode);
        await using var read = RenderDbContext.Create(connection);
        Assert.Empty(await read.Set<InboxMessageRecord>().ToArrayAsync(Token));
        await app.StopAsync(Token);
    }

    private async Task<string> DatabaseAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = RenderDbContext.Create(connection);
        await database.Database.MigrateAsync(Token);
        return connection;
    }

    internal static Uri Address(IServiceProvider services) =>
        new(
            new Uri(
                services
                    .GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!
                    .Addresses.Single()
            ),
            "/commands"
        );

    internal static async Task<InboxProcessingResult> ProcessAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxProcessor<RenderDbContext>>()
            .ProcessNextAsync(RenderExportHandler.Subscription, Token);
    }

    internal static async Task SqlAsync(string connection, string sql)
    {
        await using var database = new Npgsql.NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new Npgsql.NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }
}
