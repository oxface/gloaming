using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Messaging.Tests.Rendering;

[Collection("Rendering PostgreSQL")]
public sealed class RetentionMigrationTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task ReceiptGenerationUpgradePreservesEveryRetainedLifecycleAndEnvelope()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await postgres.CreateDatabaseAsync(token);
        await using var database = RenderDbContext.Create(connection);
        await database
            .GetService<IMigrator>()
            .MigrateAsync("20261021042518_AddInboxDeadLetterLookup", token);
        await AdoptionTests.SqlAsync(
            connection,
            """
            INSERT INTO rendering.incoming_work
                (subscription_key, producer_key, message_id, message_name, schema_version, payload,
                 processed_at, revision, failure_count, failures_since_redrive, last_failed_at, last_failure_code, quarantined_at)
            VALUES ('rendering.jobs', 'exports', '00000000-0000-0000-0000-000000000001', 'exports.render', 1,
                    '{"ExportRequestId":"00000000-0000-0000-0000-000000000011","Pages":2}', NULL, 0, 0, 0, NULL, NULL, NULL),
                   ('rendering.jobs', 'exports', '00000000-0000-0000-0000-000000000002', 'exports.render', 1,
                    '{"ExportRequestId":"00000000-0000-0000-0000-000000000012","Pages":3}', '2020-01-01Z', 1, 0, 0, NULL, NULL, NULL),
                   ('rendering.jobs', 'exports', '00000000-0000-0000-0000-000000000003', 'exports.render', 1,
                    '{"ExportRequestId":"00000000-0000-0000-0000-000000000013","Pages":4}', NULL, 1, 1, 1, '2020-01-01Z', 'review', '2020-01-01Z');
            """
        );
        string original = await database
            .Database.SqlQueryRaw<string>(
                "SELECT jsonb_agg(to_jsonb(r) ORDER BY message_id)::text AS \"Value\" FROM rendering.incoming_work r"
            )
            .SingleAsync(token);
        await database.Database.MigrateAsync(token);
        Assert.False(database.Database.HasPendingModelChanges());
        var rows = await database.Set<InboxMessageRecord>().AsNoTracking().ToArrayAsync(token);
        Assert.Equal(3, rows.Select(row => row.ReceiptId).Distinct().Count());
        Assert.All(rows, row => Assert.NotEqual(Guid.Empty, row.ReceiptId));
        string upgraded = await database
            .Database.SqlQueryRaw<string>(
                "SELECT jsonb_agg(to_jsonb(r) - 'receipt_id' ORDER BY message_id)::text AS \"Value\" FROM rendering.incoming_work r"
            )
            .SingleAsync(token);
        Assert.True(
            JsonElement.DeepEquals(
                JsonSerializer.Deserialize<JsonElement>(original),
                JsonSerializer.Deserialize<JsonElement>(upgraded)
            )
        );
        var indexes = await database
            .Database.SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'rendering' AND tablename = 'incoming_work'"
            )
            .ToArrayAsync(token);
        Assert.Contains(
            indexes,
            definition =>
                definition.Contains("processed_at, message_id", StringComparison.Ordinal)
                && definition.Contains("processed_at IS NOT NULL", StringComparison.Ordinal)
        );
    }
}
