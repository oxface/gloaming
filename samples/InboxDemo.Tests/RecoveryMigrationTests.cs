using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.InboxDemo.Tests;

[Collection("Rendering PostgreSQL")]
public sealed class RecoveryMigrationTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task DeadLetterLookupUpgradePreservesCheckpointStateAndRetry()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await postgres.CreateDatabaseAsync(token);
        await using (var checkpoint = RenderDbContext.Create(connection))
            await checkpoint
                .GetService<IMigrator>()
                .MigrateAsync("20261018183419_AddInboxFailureRecovery", token);

        await AdoptionTests.SqlAsync(
            connection,
            "ALTER TABLE rendering.jobs ADD CONSTRAINT renderer_unavailable CHECK (false) NOT VALID"
        );
        var services = new ServiceCollection();
        InboxDemoHost.Register(services, connection);
        services.AddPostgresInboxDeadLetters<RenderDbContext>();
        await using var provider = services.BuildServiceProvider();
        Guid id = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<RenderDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            await scope
                .ServiceProvider.GetRequiredService<IInbox<RenderDbContext>>()
                .ReceiveAsync(
                    RenderExportHandler.Subscription,
                    new(
                        id,
                        "exports",
                        "exports.render",
                        1,
                        JsonSerializer.SerializeToElement(
                            new { ExportRequestId = Guid.NewGuid(), Pages = 2 }
                        ),
                        correlationId: "preserved-correlation",
                        causationId: "preserved-cause"
                    ),
                    token
                );
            await transaction.CommitAsync(token);
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => AdoptionTests.ProcessAsync(provider));
        JsonElement original;
        await using (var before = RenderDbContext.Create(connection))
            original = JsonSerializer.SerializeToElement(
                await before.Set<InboxMessageRecord>().SingleAsync(token)
            );

        await using (var upgraded = RenderDbContext.Create(connection))
        {
            await upgraded.Database.MigrateAsync(token);
            Assert.False(upgraded.Database.HasPendingModelChanges());
            Assert.True(
                JsonElement.DeepEquals(
                    original,
                    JsonSerializer.SerializeToElement(
                        await upgraded.Set<InboxMessageRecord>().SingleAsync(token)
                    )
                )
            );
            var indexes = await upgraded
                .Database.SqlQueryRaw<string>(
                    "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'rendering' AND tablename = 'incoming_work'"
                )
                .ToArrayAsync(token);
            Assert.Contains(
                indexes,
                definition =>
                    definition.Contains("quarantined_at IS NOT NULL", StringComparison.Ordinal)
            );
        }

        await using var recovery = provider.CreateAsyncScope();
        var letters = recovery.ServiceProvider.GetRequiredService<
            IInboxDeadLetters<RenderDbContext>
        >();
        var retained = (
            await letters.GetAsync(RenderExportHandler.Subscription, "exports", id, token)
        )!;
        Assert.Equal(1, retained.FailureCount);
        Assert.Equal(
            InboxRetryResult.Retried,
            await letters.RetryAsync(
                RenderExportHandler.Subscription,
                "exports",
                id,
                retained.Revision,
                token
            )
        );
        await AdoptionTests.SqlAsync(
            connection,
            "ALTER TABLE rendering.jobs DROP CONSTRAINT renderer_unavailable"
        );
        Assert.Equal(InboxProcessingResult.Processed, await AdoptionTests.ProcessAsync(provider));
        await using var completed = RenderDbContext.Create(connection);
        var row = await completed.Set<InboxMessageRecord>().SingleAsync(token);
        Assert.NotNull(row.ProcessedAt);
        Assert.Equal(1, row.FailureCount);
        Assert.Equal(1, row.RedriveCount);
        Assert.True(JsonElement.DeepEquals(original.GetProperty("Payload"), row.Payload));
        Assert.Equal(original.GetProperty("ReceivedAt").GetDateTimeOffset(), row.ReceivedAt);
    }

    [Fact]
    public async Task UpgradePreservesPendingAndCompletedRowsWithoutInventingPastFailures()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await postgres.CreateDatabaseAsync(token);
        Guid pending = Guid.NewGuid(),
            completed = Guid.NewGuid();
        string payload = JsonSerializer.Serialize(
            new { ExportRequestId = Guid.NewGuid(), Pages = 2 }
        );
        await using var database = RenderDbContext.Create(connection);
        await database
            .GetService<IMigrator>()
            .MigrateAsync("20261010135437_AddMessageTraceContext", token);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO rendering.incoming_work
                (subscription_key, producer_key, message_id, message_name, schema_version, payload, processed_at)
            VALUES ('rendering.jobs', 'exports', {pending}, 'exports.render', 1, CAST({payload} AS jsonb), NULL),
                   ('rendering.jobs', 'exports', {completed}, 'exports.render', 1, CAST({payload} AS jsonb), '2026-10-10T12:00:00Z')
            """,
            token
        );
        await database.Database.MigrateAsync(token);
        Assert.False(database.Database.HasPendingModelChanges());
        var rows = await database.Set<InboxMessageRecord>().ToArrayAsync(token);
        Assert.Equal(2, rows.Length);
        Assert.All(
            rows,
            row =>
            {
                Assert.Equal(0, row.Revision);
                Assert.Equal(0, row.FailureCount);
                Assert.Equal(0, row.FailuresSinceRedrive);
                Assert.Equal(0, row.RedriveCount);
                Assert.Null(row.DeadLetteredAt);
                Assert.Null(row.LastFailedAt);
                Assert.Null(row.LastFailureCode);
                Assert.Null(row.LastRedrivenAt);
                Assert.Equal("exports.render", row.MessageName);
                Assert.True(
                    JsonElement.DeepEquals(
                        JsonSerializer.Deserialize<JsonElement>(payload),
                        row.Payload
                    )
                );
            }
        );
        Assert.Null(rows.Single(row => row.MessageId == pending).ProcessedAt);
        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-10-10T12:00:00Z",
                System.Globalization.CultureInfo.InvariantCulture
            ),
            rows.Single(row => row.MessageId == completed).ProcessedAt
        );
    }
}
