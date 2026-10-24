using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Worker.Broker;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed class OutboxMigrationTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    [Theory]
    [InlineData("sales", "20261021103653_AddOutboxDeadLetters")]
    [InlineData("inventory", "20261021103629_AddOutboxDeadLetters")]
    public async Task ReceiptGenerationUpgradePreservesLegacyInboxKeysEnvelopesAndRecoveryStates(
        string schema,
        string checkpoint
    )
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        await using var scope = proof.Scope();
        DbContext database =
            schema == "sales"
                ? scope.ServiceProvider.GetRequiredService<SalesDbContext>()
                : scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await database.GetService<IMigrator>().MigrateAsync(checkpoint, token);
        using var identifiers = new NpgsqlCommandBuilder();
        string table = identifiers.QuoteIdentifier(schema) + ".inbox_messages";
        await using var native = new NpgsqlConnection(database.Database.GetConnectionString());
        await native.OpenAsync(token);
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO {table}
                (subscription_key, producer_key, message_id, message_name, schema_version, payload, tenant_key,
                 processed_at, revision, failure_count, failures_since_redrive, last_failed_at, last_failure_code, quarantined_at)
            VALUES ('retention-proof', 'producer-proof', gen_random_uuid(), 'legacy', 1, jsonb_build_object('preserved', true), 'wholesale-alpha',
                    NULL, 0, 0, 0, NULL, NULL, NULL),
                   ('retention-proof', 'producer-proof', gen_random_uuid(), 'legacy', 1, jsonb_build_object('preserved', true), 'wholesale-alpha',
                    '2020-01-01Z', 1, 0, 0, NULL, NULL, NULL),
                   ('retention-proof', 'producer-proof', gen_random_uuid(), 'legacy', 1, jsonb_build_object('preserved', true), 'wholesale-beta',
                    NULL, 1, 1, 1, '2020-01-01Z', 'review', '2020-01-01Z');
            """,
            native
        );
        await command.ExecuteNonQueryAsync(token);
        command.CommandText =
            $"SELECT jsonb_agg(to_jsonb(r) ORDER BY message_id)::text FROM {table} r";
        string before = (string)(await command.ExecuteScalarAsync(token))!;
        await database.Database.MigrateAsync(token);
        Assert.False(database.Database.HasPendingModelChanges());
        command.CommandText =
            $"SELECT jsonb_agg(to_jsonb(r) - 'receipt_id' ORDER BY message_id)::text FROM {table} r";
        string after = (string)(await command.ExecuteScalarAsync(token))!;
        Assert.True(
            JsonElement.DeepEquals(
                JsonSerializer.Deserialize<JsonElement>(before),
                JsonSerializer.Deserialize<JsonElement>(after)
            )
        );
        command.CommandText =
            $"SELECT count(DISTINCT receipt_id) = count(*) AND bool_and(receipt_id <> '00000000-0000-0000-0000-000000000000'::uuid) FROM {table}";
        Assert.Equal(true, await command.ExecuteScalarAsync(token));
    }

    [Fact]
    public async Task SalesAndInventoryUpgradePreserveAllLegacyOutboxStateAndSnapshotsMatch()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        await using var scope = proof.Scope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await VerifyAsync(sales, "sales", "20261021042524_AddInboxDeadLetterLookup", token);
        await VerifyAsync(inventory, "inventory", "20261021042521_AddInboxDeadLetterLookup", token);
    }

    private static async Task VerifyAsync(
        DbContext database,
        string schema,
        string checkpoint,
        CancellationToken token
    )
    {
        await database.GetService<IMigrator>().MigrateAsync(checkpoint, token);
        using var identifiers = new NpgsqlCommandBuilder();
        string table = identifiers.QuoteIdentifier(schema) + ".outbox_messages";
        await using var native = new NpgsqlConnection(database.Database.GetConnectionString());
        await native.OpenAsync(token);
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO {table} (message_id, destination, message_name, schema_version, payload, owner_key,
                attempts, lease_token, lease_until, dispatched_at)
            VALUES ('00000000-0000-0000-0000-000000000001', 'legacy', 'legacy', 1, jsonb_build_object('preserved', true), 'wholesale-alpha',
                    3, NULL, NULL, NULL),
                   ('00000000-0000-0000-0000-000000000002', 'legacy', 'legacy', 1, jsonb_build_object('preserved', true), 'wholesale-beta',
                    4, '00000000-0000-0000-0000-000000000004', clock_timestamp() + interval '1 minute', NULL),
                   ('00000000-0000-0000-0000-000000000003', 'legacy', 'legacy', 1, jsonb_build_object('preserved', true), 'wholesale-alpha',
                    7, NULL, NULL, '2026-10-10T12:00:00Z')
            """,
            native
        );
        await command.ExecuteNonQueryAsync(token);
        command.CommandText =
            $"SELECT jsonb_agg(to_jsonb(q) ORDER BY message_id)::text FROM {table} q";
        string before = (string)(await command.ExecuteScalarAsync(token))!;
        await database.Database.MigrateAsync(token);
        Assert.False(database.Database.HasPendingModelChanges());
        command.CommandText = $"""
            SELECT jsonb_agg(to_jsonb(q) - ARRAY['revision','failure_count','failures_since_redrive',
                'last_failed_at','last_failure_code','dead_lettered_at','redrive_count','last_redriven_at'] ORDER BY message_id)::text AS "Value"
            FROM {table} q
            """;
        string after = (string)(await command.ExecuteScalarAsync(token))!;
        Assert.True(
            JsonElement.DeepEquals(
                JsonSerializer.Deserialize<JsonElement>(before),
                JsonSerializer.Deserialize<JsonElement>(after)
            )
        );
        Assert.All(
            await database
                .Set<OutboxMessageRecord>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .ToArrayAsync(token),
            row =>
            {
                Assert.Equal(0, row.FailureCount);
                Assert.Equal(0, row.Revision);
                Assert.Equal(0, row.FailuresSinceRedrive);
                Assert.Equal(0, row.RedriveCount);
                Assert.Null(row.DeadLetteredAt);
                Assert.Null(row.LastFailedAt);
                Assert.Null(row.LastFailureCode);
                Assert.Null(row.LastRedrivenAt);
            }
        );
    }
}
