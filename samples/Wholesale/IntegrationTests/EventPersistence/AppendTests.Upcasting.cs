using System.Diagnostics;
using System.Text.Json;
using Gloaming.Events.Serialization;
using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.EventPersistence;

public sealed partial class AppendTests
{
    private static readonly int[] MixedSchemaVersions = [1, 1, 1, 2];

    [Theory]
    [InlineData("none")]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task MixedSchemasReadAndRebuildWithoutRewritingAnyStoredFact(string damage)
    {
        string connection = await SeedMixedSchemasAsync();
        string facts = await StoredFactsAsync(connection);
        using (var document = JsonDocument.Parse(facts))
        {
            var rows = document.RootElement.EnumerateArray().ToArray();
            Assert.Equal(
                MixedSchemaVersions,
                rows.Select(row => row.GetProperty("schema_version").GetInt32())
            );
            Assert.Equal(
                10.125m,
                rows[1].GetProperty("payload").GetProperty("quantity").GetDecimal()
            );
            Assert.Equal(
                2m,
                rows[3].GetProperty("payload").GetProperty("receivedQuantity").GetDecimal()
            );
            Assert.False(rows[3].GetProperty("payload").TryGetProperty("quantity", out _));
        }

        await using var provider = RebuildProvider(connection);
        await AssertReadAsync(provider, (4, 15m));
        await using (var read = Scope(provider, Alpha))
        {
            var history = read.ServiceProvider.GetRequiredService<IStockPositionHistory>();
            Assert.Equal(10.125m, (await history.ReadAtVersionAsync(Id, 2, Token))!.OnHand);
            Assert.Equal(
                "DELIVERY-2",
                (await history.ReadAtVersionAsync(Id, 3, Token))!.LatestDeliveryReference
            );
            Assert.Equal(10.125m, (await history.ReadAsOfAsync(Id, Changed, Token))!.OnHand);
            Assert.Empty(Context(read).ChangeTracker.Entries());
        }

        if (damage == "missing")
            await ExecuteAsync(connection, "DELETE FROM inventory.stock_position_current");
        if (damage == "corrupt")
            await ExecuteAsync(
                connection,
                "UPDATE inventory.stock_position_current SET state = '[]'::jsonb"
            );
        await using (var repair = Scope(provider, Alpha))
        {
            var database = Context(repair);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                (4, FixtureHistories.LastStockChangeAt.AddSeconds(10)),
                Rebuilt(await RebuildAsync(repair))
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (4, 15m));
        Assert.Equal(facts, await StoredFactsAsync(connection));
    }

    [Fact]
    public async Task IndependentlyAuthoredV2LiteralHasTheSameReceiptMeaning()
    {
        string connection = await DatabaseAsync();
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(fixtures, "Inventory", "received.v2.json"))
        );
        JsonElement envelope = document.RootElement;
        var receipt = new SerializedEvent(
            envelope.GetProperty("eventName").GetString()!,
            envelope.GetProperty("schemaVersion").GetInt32(),
            envelope.GetProperty("payload").Clone()
        );
        await using var provider = Provider(connection);
        await using (var seed = Scope(provider, Alpha))
        {
            var database = seed.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            InventoryHistorySeed.Add(
                database,
                Id,
                [FixtureHistories.Inventory(fixtures)[0], (receipt, Changed)]
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (2, 10.125m));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"quantity\":\"bad\"}")]
    [InlineData("{\"quantity\":null}")]
    public async Task RejectedOldPayloadLeavesTrackingAndStorageUntouchedThenFreshRepairRecovers(
        string invalid
    )
    {
        string connection = await SeedMixedSchemasAsync();
        string originalFacts = await StoredFactsAsync(connection);
        await ReplaceOldPayloadAsync(connection, invalid);
        await ExecuteAsync(
            connection,
            "UPDATE inventory.stock_position_current SET state = '[]'::jsonb"
        );
        string damagedFacts = await StoredFactsAsync(connection);
        string damagedState = await StoredAggregateAsync(connection);
        await using var provider = RebuildProvider(connection);
        await using (var repair = Scope(provider, Alpha))
        {
            var database = Context(repair);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var failure = await Assert.ThrowsAsync<EventDecodingException>(() =>
                RebuildAsync(repair)
            );
            Assert.Equal(EventDecodingFailure.InvalidPayload, failure.Failure);
            Assert.Equal("inventory.stock-position.received", failure.EventName);
            Assert.Equal(1, failure.SchemaVersion);
            Assert.IsType<JsonException>(failure.InnerException);
            Assert.Empty(database.ChangeTracker.Entries());
            await transaction.RollbackAsync(Token);
        }

        Assert.Equal(damagedFacts, await StoredFactsAsync(connection));
        Assert.Equal(damagedState, await StoredAggregateAsync(connection));
        await using (var read = Scope(provider, Alpha))
            await Assert.ThrowsAsync<EventDecodingException>(() =>
                read
                    .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                    .ReadCurrentAsync(Id, Token)
            );
        await ReplaceOldPayloadAsync(connection, """{"quantity":10.125}""");
        await RepairMixedAsync(provider);
        await AssertReadAsync(provider, (4, 15m));
        Assert.Equal(originalFacts, await StoredFactsAsync(connection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedSchemaRepairRollbackOrSqlFailurePreservesDamageAndFreshRepairRecovers(
        bool failSave
    )
    {
        string connection = await SeedMixedSchemasAsync();
        await ExecuteAsync(
            connection,
            "UPDATE inventory.stock_position_current SET state = '[]'::jsonb"
        );
        string facts = await StoredFactsAsync(connection);
        string damagedState = await StoredAggregateAsync(connection);
        if (failSave)
            await ExecuteAsync(
                connection,
                "ALTER TABLE inventory.stock_position_current ADD CONSTRAINT upcast_repair_fault CHECK (state = '[]'::jsonb) NOT VALID"
            );
        await using var provider = RebuildProvider(connection);
        await using (var repair = Scope(provider, Alpha))
        {
            var database = Context(repair);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Rebuilt(await RebuildAsync(repair));
            if (failSave)
                await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync(Token));
            else
                await database.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }

        Assert.Equal(facts, await StoredFactsAsync(connection));
        Assert.Equal(damagedState, await StoredAggregateAsync(connection));
        if (failSave)
            await ExecuteAsync(
                connection,
                "ALTER TABLE inventory.stock_position_current DROP CONSTRAINT upcast_repair_fault"
            );
        await RepairMixedAsync(provider);
        await AssertReadAsync(provider, (4, 15m));
        Assert.Equal(facts, await StoredFactsAsync(connection));
    }

    [Fact]
    public async Task SchemaEvolutionExecutableJourneyUsesNativeSaveAndCanRunAgain()
    {
        string connection = await DatabaseAsync();
        string expected =
            "inventory schemas: retained-v1=10.125, current-version=4, inline=15.000, rebuilt=15.000"
            + Environment.NewLine;
        for (int run = 0; run < 2; run++)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(typeof(SchemaEvolutionJourney).Assembly.Location);
            start.ArgumentList.Add("--scenario");
            start.ArgumentList.Add("schema-evolution");
            start.Environment["WHOLESALE_SCENARIO_CONNECTION"] = connection;
            using var process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(Token);
            Task<string> error = process.StandardError.ReadToEndAsync(Token);
            await process.WaitForExitAsync(Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await error);
            Assert.Equal(expected, await output);
        }
    }

    private async Task<string> SeedMixedSchemasAsync()
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        await using (var seed = Scope(provider, Alpha))
        {
            var database = seed.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            InventoryHistorySeed.Add(
                database,
                Id,
                FixtureHistories.Inventory(Path.Combine(AppContext.BaseDirectory, "Fixtures"))
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                (4, 15m),
                Proposed(
                    await AppendAsync(
                        write,
                        3,
                        [2m],
                        FixtureHistories.LastStockChangeAt.AddSeconds(10)
                    )
                )
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        return connection;
    }

    private static async Task RepairMixedAsync(ServiceProvider provider)
    {
        await using var repair = Scope(provider, Alpha);
        var database = Context(repair);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Rebuilt(await RebuildAsync(repair));
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static Task<string> StoredFactsAsync(string connection) =>
        StoredJsonAsync(
            connection,
            "SELECT jsonb_agg(to_jsonb(e) ORDER BY stream_version)::text FROM inventory.events AS e"
        );

    private static Task<string> StoredAggregateAsync(string connection) =>
        StoredJsonAsync(
            connection,
            "SELECT jsonb_build_object('stream', to_jsonb(s), 'state', to_jsonb(a))::text FROM inventory.event_streams AS s JOIN inventory.stock_position_current AS a ON a.organization_key = s.organization_key AND a.stream_id = s.id"
        );

    private static async Task<string> StoredJsonAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        return (string)(await command.ExecuteScalarAsync(Token))!;
    }

    private static async Task ReplaceOldPayloadAsync(string connection, string json)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "UPDATE inventory.events SET payload = @payload::jsonb WHERE stream_version = 2",
            database
        );
        command.Parameters.AddWithValue("payload", json);
        await command.ExecuteNonQueryAsync(Token);
    }
}
