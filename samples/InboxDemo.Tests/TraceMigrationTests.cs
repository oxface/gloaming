using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.InboxDemo.Tests;

[Collection("Rendering PostgreSQL")]
public sealed class TraceMigrationTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task UpgradePreservesAndProcessesHistoricalIntakeWithoutTraceContext()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await postgres.CreateDatabaseAsync(token);
        Guid message = Guid.NewGuid(),
            export = Guid.NewGuid();
        string payload = JsonSerializer.Serialize(new { ExportRequestId = export, Pages = 3 });
        await using (var legacy = RenderDbContext.Create(connection))
        {
            await legacy
                .GetService<IMigrator>()
                .MigrateAsync("20261008213953_InitialRendering", token);
            await legacy.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO rendering.incoming_work (subscription_key, producer_key, message_id, message_name, schema_version, payload)
                VALUES ('rendering.jobs', 'exports', {message}, 'exports.render', 1, CAST({payload} AS jsonb))
                """,
                token
            );
            await legacy.Database.MigrateAsync(token);
            Assert.False(legacy.Database.HasPendingModelChanges());
            var row = await legacy.Set<InboxMessageRecord>().SingleAsync(token);
            Assert.Null(row.TraceParent);
            Assert.Null(row.TraceState);
            Assert.Null(row.ProcessedAt);
        }

        var services = new ServiceCollection();
        InboxDemoHost.Register(services, connection);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(
            InboxProcessingResult.Processed,
            await scope
                .ServiceProvider.GetRequiredService<IInboxProcessor<RenderDbContext>>()
                .ProcessNextAsync(RenderExportHandler.Subscription, token)
        );
        await using var fresh = RenderDbContext.Create(connection);
        Assert.Equal(export, (await fresh.Set<RenderJob>().SingleAsync(token)).ExportRequestId);
        Assert.NotNull((await fresh.Set<InboxMessageRecord>().SingleAsync(token)).ProcessedAt);
    }
}
