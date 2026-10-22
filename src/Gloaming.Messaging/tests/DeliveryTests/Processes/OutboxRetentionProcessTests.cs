using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Messaging.Exports;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Messaging.Tests.Processes;

public sealed class OutboxRetentionProcessTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact(Timeout = 90_000)]
    public async Task FiniteCleanupUsesSeparateMetadataOnlyAuthorityAndRejectsOwnerCredentials()
    {
        var token = TestContext.Current.CancellationToken;
        var (connection, maintenance) = await SetupAsync(token);
        await using (var denied = Command(connection))
        {
            Assert.NotEqual(0, await denied.WaitForExitAsync(token));
            Assert.Contains("maintenance role", denied.Output, StringComparison.OrdinalIgnoreCase);
        }

        await using (var cleanup = Command(maintenance))
        {
            Assert.True(await cleanup.WaitForExitAsync(token) == 0, cleanup.Output);
            Assert.Contains("\"Deleted\":1", cleanup.Output, StringComparison.Ordinal);
        }

        await using var read = ExportDbContext.Create(connection);
        Assert.Equal(
            1,
            await read.Set<OutboxMessageRecord>().CountAsync(row => row.DispatchedAt != null, token)
        );
        Assert.Equal(
            1,
            await read.Set<OutboxMessageRecord>().CountAsync(row => row.DispatchedAt == null, token)
        );
    }

    [Fact(Timeout = 90_000)]
    public async Task KillDuringDeleteRollsBackAndFreshProcessCanRepeatTheBoundedCleanup()
    {
        var token = TestContext.Current.CancellationToken;
        var (connection, maintenance) = await SetupAsync(token);
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync(token);
        await using (
            var install = new NpgsqlCommand(
                """
                SELECT pg_advisory_lock(281002);
                CREATE FUNCTION exports.hold_cleanup() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN PERFORM pg_advisory_xact_lock(281002); RETURN OLD; END; $$;
                CREATE TRIGGER hold_cleanup BEFORE DELETE ON exports.outgoing_work FOR EACH ROW EXECUTE FUNCTION exports.hold_cleanup();
                """,
                barrier
            )
        )
            await install.ExecuteNonQueryAsync(token);
        string named = new NpgsqlConnectionStringBuilder(maintenance)
        {
            ApplicationName = "cleanup-before-death",
        }.ConnectionString;
        await using (var killed = Command(named))
        {
            await WaitAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'cleanup-before-death' AND wait_event = 'advisory')",
                token
            );
            await killed.KillAsync(token);
        }

        await using (var release = new NpgsqlCommand("SELECT pg_advisory_unlock(281002)", barrier))
            await release.ExecuteNonQueryAsync(token);
        await WaitAsync(
            connection,
            "SELECT NOT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'cleanup-before-death')",
            token
        );
        await using var read = ExportDbContext.Create(connection);
        Assert.Equal(
            2,
            await read.Set<OutboxMessageRecord>().CountAsync(row => row.DispatchedAt != null, token)
        );
        await read.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER hold_cleanup ON exports.outgoing_work",
            token
        );
        await using var restarted = Command(maintenance);
        Assert.True(await restarted.WaitForExitAsync(token) == 0, restarted.Output);
        Assert.Contains("\"Deleted\":1", restarted.Output, StringComparison.Ordinal);
        Assert.Equal(
            1,
            await read.Set<OutboxMessageRecord>().CountAsync(row => row.DispatchedAt != null, token)
        );
        Assert.Equal(
            1,
            await read.Set<OutboxMessageRecord>().CountAsync(row => row.DispatchedAt == null, token)
        );
    }

    private async Task<(string Owner, string Maintenance)> SetupAsync(CancellationToken token)
    {
        string connection = await postgres.CreateDatabaseAsync(token);
        await using (var database = ExportDbContext.Create(connection))
        {
            await database.Database.MigrateAsync(token);
            await database.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO exports.outgoing_work(message_id, destination, message_name, schema_version, payload, dispatched_at, attempts)
                VALUES (gen_random_uuid(), 'exports.render', 'exports.render', 1, jsonb_build_object('pages', 1), '2020-01-01Z', 1),
                       (gen_random_uuid(), 'exports.render', 'exports.render', 1, jsonb_build_object('pages', 1), '2020-01-01Z', 1),
                       (gen_random_uuid(), 'exports.render', 'exports.render', 1, jsonb_build_object('pages', 1), NULL, 0);
                """,
                token
            );
        }

        string role = "cleanup_" + Guid.NewGuid().ToString("N");
        await using (var owner = new NpgsqlConnection(connection))
        {
            await owner.OpenAsync(token);
            await using var grants = new NpgsqlCommand(
                $"""
                CREATE ROLE "{role}" LOGIN PASSWORD 'maintenance-proof' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT;
                GRANT USAGE ON SCHEMA exports TO "{role}";
                GRANT SELECT(message_id, destination, dispatched_at, dead_lettered_at, lease_token, lease_until)
                    ON exports.outgoing_work TO "{role}";
                GRANT DELETE ON exports.outgoing_work TO "{role}";
                """,
                owner
            );
            await grants.ExecuteNonQueryAsync(token);
        }

        string maintenance = new NpgsqlConnectionStringBuilder(connection)
        {
            Username = role,
            Password = "maintenance-proof",
        }.ConnectionString;
        return (connection, maintenance);
    }

    [Theory(Timeout = 90_000)]
    [InlineData("payload")]
    [InlineData("public-update")]
    [InlineData("business")]
    public async Task ExpandedEffectivePrivilegesRejectBeforeAnyCleanup(string privilege)
    {
        var token = TestContext.Current.CancellationToken;
        var (connection, maintenance) = await SetupAsync(token);
        string role = new NpgsqlConnectionStringBuilder(maintenance).Username!;
        using var identifiers = new NpgsqlCommandBuilder();
        string quoted = identifiers.QuoteIdentifier(role);
        string sql = privilege switch
        {
            "payload" => $"GRANT SELECT(payload) ON exports.outgoing_work TO {quoted}",
            "public-update" => "GRANT UPDATE(revision) ON exports.outgoing_work TO PUBLIC",
            _ => $"GRANT SELECT ON exports.requests TO {quoted}",
        };
        await using (var owner = ExportDbContext.Create(connection))
            await owner.Database.ExecuteSqlRawAsync(sql, token);
        await using var denied = Command(maintenance);
        Assert.NotEqual(0, await denied.WaitForExitAsync(token));
        Assert.Contains("maintenance role", denied.Output, StringComparison.OrdinalIgnoreCase);
        await using var read = ExportDbContext.Create(connection);
        Assert.Equal(
            2,
            await read.Set<OutboxMessageRecord>().CountAsync(row => row.DispatchedAt != null, token)
        );
    }

    private static async Task WaitAsync(
        string connection,
        string condition,
        CancellationToken token
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var database = new NpgsqlConnection(connection);
            await database.OpenAsync(deadline.Token);
            await using var command = new NpgsqlCommand(condition, database);
            if (await command.ExecuteScalarAsync(deadline.Token) is true)
                return;

            await Task.Delay(20, deadline.Token);
        }
    }

    private static ChildHost Command(string connection) =>
        new(
            typeof(DemoJourneys).Assembly,
            new()
            {
                ["OUTBOX_DEMO_MAINTENANCE_CONNECTION_STRING"] = connection,
                ["OUTBOX_DEMO_COMPLETED_BEFORE"] = "2020-01-02T00:00:00Z",
                ["OUTBOX_DEMO_CLEANUP_BATCH_SIZE"] = "1",
            },
            "--cleanup"
        );
}
