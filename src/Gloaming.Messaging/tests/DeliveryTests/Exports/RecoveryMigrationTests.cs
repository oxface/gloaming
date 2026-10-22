using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Messaging.Tests.Exports;

public sealed class RecoveryMigrationTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task ForwardMigrationPreservesPendingLeasedAndCompletedWorkWithoutInventingFailures()
    {
        var token = TestContext.Current.CancellationToken;
        string connection = await postgres.CreateDatabaseAsync(token);
        await using var database = ExportDbContext.Create(connection);
        await database
            .GetService<IMigrator>()
            .MigrateAsync("20261010135315_AddMessageTraceContext", token);
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO exports.outgoing_work (message_id, destination, message_name, schema_version, payload,
                owner_key, correlation_id, causation_id, trace_parent, trace_state, attempts,
                lease_token, lease_until, dispatched_at)
            VALUES ('00000000-0000-0000-0000-000000000001', 'exports.render', 'exports.render', 1, jsonb_build_object('pages', 3),
                    'owner', 'correlation', 'cause', 'retained-parent', 'retained-state', 3, NULL, NULL, NULL),
                   ('00000000-0000-0000-0000-000000000002', 'exports.render', 'exports.render', 1, jsonb_build_object('pages', 3),
                    NULL, NULL, NULL, NULL, NULL, 4, '00000000-0000-0000-0000-000000000004', clock_timestamp() + interval '1 minute', NULL),
                   ('00000000-0000-0000-0000-000000000003', 'exports.render', 'exports.render', 1, jsonb_build_object('pages', 3),
                    NULL, NULL, NULL, NULL, NULL, 7, NULL, NULL, '2026-10-10T12:00:00Z');
            """,
            token
        );
        string before = await database
            .Database.SqlQueryRaw<string>(
                "SELECT jsonb_agg(to_jsonb(q) ORDER BY message_id)::text AS \"Value\" FROM exports.outgoing_work q"
            )
            .SingleAsync(token);
        await database.Database.MigrateAsync(token);
        Assert.False(database.Database.HasPendingModelChanges());
        string after = await database
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_agg(to_jsonb(q) - ARRAY['revision','failure_count','failures_since_redrive',
                    'last_failed_at','last_failure_code','dead_lettered_at','redrive_count','last_redriven_at'] ORDER BY message_id)::text AS "Value"
                FROM exports.outgoing_work q
                """
            )
            .SingleAsync(token);
        Assert.True(
            JsonElement.DeepEquals(
                JsonSerializer.Deserialize<JsonElement>(before),
                JsonSerializer.Deserialize<JsonElement>(after)
            )
        );
        Assert.All(
            await database.Set<OutboxMessageRecord>().ToArrayAsync(token),
            row =>
            {
                Assert.Equal(0, row.Revision);
                Assert.Equal(0, row.FailureCount);
                Assert.Equal(0, row.FailuresSinceRedrive);
                Assert.Equal(0, row.RedriveCount);
                Assert.Null(row.LastFailedAt);
                Assert.Null(row.LastFailureCode);
                Assert.Null(row.DeadLetteredAt);
                Assert.Null(row.LastRedrivenAt);
            }
        );
    }
}
