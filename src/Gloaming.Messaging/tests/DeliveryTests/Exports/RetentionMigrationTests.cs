using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gloaming.Samples.Messaging.Tests.Exports;

public sealed class RetentionMigrationTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task ForwardIndexMigrationPreservesExistingRowsAndSupportsTheBoundedCleanup()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await postgres.CreateDatabaseAsync(token);
        await using var database = ExportDbContext.Create(connection);
        await database
            .GetService<IMigrator>()
            .MigrateAsync("20261021103539_AddOutboxDeadLetters", token);
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO exports.outgoing_work(message_id, destination, message_name, schema_version, payload, dispatched_at, attempts)
            VALUES (gen_random_uuid(), 'exports.render', 'exports.render', 1, jsonb_build_object('pages', 1), '2020-01-01Z', 1),
                   (gen_random_uuid(), 'exports.render', 'exports.render', 1, jsonb_build_object('pages', 2), NULL, 0);
            """,
            token
        );
        string before = await RowsAsync();
        await database.Database.MigrateAsync(token);
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.True(
            JsonElement.DeepEquals(
                JsonSerializer.Deserialize<JsonElement>(before),
                JsonSerializer.Deserialize<JsonElement>(await RowsAsync())
            )
        );
        string definition = await database
            .Database.SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value" FROM pg_indexes WHERE schemaname = 'exports'
                    AND indexname = 'ix_exports_outbox_completed_retention'
                """
            )
            .SingleAsync(token);
        Assert.Contains(
            "(destination, dispatched_at, message_id)",
            definition,
            StringComparison.Ordinal
        );
        Assert.Contains("WHERE (dispatched_at IS NOT NULL)", definition, StringComparison.Ordinal);
        Assert.Equal(
            1,
            await database.DeleteCompletedPostgresOutboxBatchAsync(
                "exports.render",
                new(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
                1,
                token
            )
        );

        Task<string> RowsAsync() =>
            database
                .Database.SqlQueryRaw<string>(
                    "SELECT jsonb_agg(to_jsonb(q) ORDER BY message_id)::text AS \"Value\" FROM exports.outgoing_work q"
                )
                .SingleAsync(token);
    }
}
