using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.InboxDemo.Tests;

[Collection("Rendering PostgreSQL")]
public sealed class RecoveryMigrationTests(PostgreSqlFixture postgres)
{
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
                Assert.Null(row.QuarantinedAt);
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
