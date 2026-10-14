using System.Diagnostics;
using Gloaming.Events.History;
using Gloaming.Events.Serialization;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo.Tests;

public sealed class HistoryReadTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string Alpha = "wholesale-alpha";
    private const string Beta = "wholesale-beta";
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothModulesReconstructSelectedRangesWithinTheCurrentTenant(bool inventory)
    {
        string connection = await SeedAsync();
        var observation = new QueryObservation(Schema(inventory));
        await using var provider = Provider(connection, observation);
        foreach (var (owner, factor) in new[] { (Alpha, 1m), (Beta, 2m) })
        {
            await using var scope = Scope(provider, owner);
            Assert.Equal(
                (3, (inventory ? 13m : 62.50m) * factor),
                Amount(await ReadAsync(scope, inventory))
            );
            Assert.Equal(
                (2, (inventory ? 10.125m : 31.25m) * factor),
                Amount(await ReadAsync(scope, inventory, version: 2))
            );
            Assert.Equal(
                (inventory ? 2 : 3, (inventory ? 10.125m : 62.50m) * factor),
                Amount(
                    await ReadAsync(
                        scope,
                        inventory,
                        cutoff: FixtureHistories.FirstChangeAt.ToOffset(TimeSpan.FromHours(2))
                    )
                )
            );
            Assert.Equal(
                (1, 0m),
                Amount(
                    await ReadAsync(
                        scope,
                        inventory,
                        cutoff: FixtureHistories.OpenedAt.AddSeconds(5)
                    )
                )
            );
            Assert.Null(
                await ReadAsync(scope, inventory, cutoff: FixtureHistories.OpenedAt.AddTicks(-1))
            );
            Assert.Null(await ReadAsync(scope, inventory, id: Guid.NewGuid()));
            Assert.Equal(
                (3, (inventory ? 13m : 62.50m) * factor),
                Amount(await ReadAsync(scope, inventory))
            );
            if (inventory)
            {
                var state = (
                    await scope
                        .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                        .ReadCurrentAsync(FixtureHistories.StreamId, Token)
                )!;
                Assert.Equal(FixtureHistories.StreamId, state.Id);
                Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), state.StockItemId);
                Assert.Equal(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    state.StockingLocationId
                );
                Assert.Equal("EA", state.BaseUnitCode);
                Assert.Equal("DELIVERY-2", state.LatestDeliveryReference);
                Assert.Equal(FixtureHistories.LastStockChangeAt, state.RecordedAt);
            }
            else
            {
                var state = (
                    await scope
                        .ServiceProvider.GetRequiredService<IPurchaseOrderHistory>()
                        .ReadCurrentAsync(FixtureHistories.StreamId, Token)
                )!;
                Assert.Equal(FixtureHistories.StreamId, state.Id);
                Assert.Equal("OLD-1", state.Code);
                Assert.Equal("SUP-1", state.SupplierReference);
                Assert.Equal("EUR", state.Currency);
                Assert.Equal(
                    new PurchaseOrderLine("ITEM-1", 5m * factor, 12.5m),
                    Assert.Single(state.Lines)
                );
                Assert.Equal(FixtureHistories.FirstChangeAt, state.RecordedAt);
            }
            Assert.Empty(Context(scope, inventory).ChangeTracker.Entries());
        }
        // Actual executed predicates, not a full SQL formatting snapshot.
        var versionQuery = observation.Commands.First(command =>
            command.Sql.Contains("ORDER BY", StringComparison.Ordinal)
            && command.Values.Contains(2L)
        );
        Assert.Contains("organization_key =", versionQuery.Sql, StringComparison.Ordinal);
        Assert.Contains("stream_id =", versionQuery.Sql, StringComparison.Ordinal);
        Assert.Contains("stream_version <=", versionQuery.Sql, StringComparison.Ordinal);
        Assert.Contains(
            "stream_version",
            versionQuery.Sql[(versionQuery.Sql.IndexOf("ORDER BY", StringComparison.Ordinal))..],
            StringComparison.Ordinal
        );
        Assert.Contains(Alpha, versionQuery.Values);
        Assert.Contains(FixtureHistories.StreamId, versionQuery.Values);
        var timeQuery = Assert.Single(
            observation.Commands,
            command =>
                command.Sql.Contains("max(", StringComparison.Ordinal)
                && command.Values.Contains(Alpha)
                && command.Values.Contains(FixtureHistories.OpenedAt.AddSeconds(5))
        );
        Assert.Contains("recorded_at <=", timeQuery.Sql, StringComparison.Ordinal);
        Assert.Contains("stream_version <=", timeQuery.Sql, StringComparison.Ordinal);
        Assert.Contains(3L, timeQuery.Values);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACommitAfterHeadCaptureIsExcludedUntilAFreshRead(bool inventory)
    {
        string connection = await SeedAsync();
        var observation = new QueryObservation(Schema(inventory))
        {
            BeforeEventRead = cancellation =>
                AppendFourthFixtureAsync(connection, inventory, cancellation),
        };
        await using var provider = Provider(connection, observation);
        await using (var scope = Scope(provider, Alpha))
            Assert.Equal((3, inventory ? 13m : 62.50m), Amount(await ReadAsync(scope, inventory)));
        await using var fresh = Scope(provider, Alpha);
        Assert.Equal((4, inventory ? 18m : 87.50m), Amount(await ReadAsync(fresh, inventory)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingAndTenantlessContextRejectReadsAndFutureVersionsAreNotClamped(
        bool inventory
    )
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var missing = provider.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReadAsync(missing, inventory)
            );
        await using (var tenantless = provider.CreateAsyncScope())
        {
            tenantless
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
            await Assert.ThrowsAsync<TenantRequiredException>(() =>
                ReadAsync(tenantless, inventory)
            );
        }
        await using var alpha = Scope(provider, Alpha);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ReadAsync(alpha, inventory, version: 0)
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ReadAsync(alpha, inventory, version: 4)
        );
        await using var stranger = Scope(provider, "foreign-owner");
        Assert.Null(await ReadAsync(stranger, inventory));
    }

    [Theory]
    [InlineData(true, "gap")]
    [InlineData(false, "gap")]
    [InlineData(true, "tail")]
    [InlineData(false, "tail")]
    [InlineData(true, "regression")]
    [InlineData(false, "regression")]
    [InlineData(true, "unknown")]
    [InlineData(false, "unknown")]
    [InlineData(true, "payload")]
    [InlineData(false, "payload")]
    [InlineData(true, "creation")]
    [InlineData(false, "creation")]
    [InlineData(true, "update")]
    [InlineData(false, "update")]
    [InlineData(true, "sequence")]
    [InlineData(false, "sequence")]
    public async Task DamagedSelectedHistoryFailsAtItsOwnBoundary(bool inventory, string fault)
    {
        string connection = await SeedAsync();
        string schema = Schema(inventory);
        string statement = fault switch
        {
            "gap" =>
                $"DELETE FROM {schema}.events WHERE organization_key = @owner AND stream_version = 2",
            "tail" =>
                $"DELETE FROM {schema}.events WHERE organization_key = @owner AND stream_version = 3",
            "regression" =>
                $"UPDATE {schema}.events SET recorded_at = @time WHERE organization_key = @owner AND stream_version = 3",
            "unknown" =>
                $"UPDATE {schema}.events SET event_name = 'unregistered' WHERE organization_key = @owner AND stream_version = 3",
            "payload" =>
                $"UPDATE {schema}.events SET payload = '{{}}'::jsonb WHERE organization_key = @owner AND stream_version = 3",
            "creation" =>
                $"UPDATE {schema}.event_streams SET created_at = @time WHERE organization_key = @owner",
            "update" =>
                $"UPDATE {schema}.event_streams SET updated_at = @time WHERE organization_key = @owner",
            "sequence" =>
                $"UPDATE {schema}.events SET event_name = (SELECT event_name FROM {schema}.events WHERE organization_key = @owner AND stream_version = 1), payload = (SELECT payload FROM {schema}.events WHERE organization_key = @owner AND stream_version = 1) WHERE organization_key = @owner AND stream_version = 3",
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        await ExecuteAsync(
            connection,
            statement,
            fault == "update"
                ? FixtureHistories.OpenedAt.AddSeconds(30)
                : FixtureHistories.OpenedAt.AddSeconds(5)
        );
        await using var provider = Provider(connection);
        await using var scope = Scope(provider, Alpha);
        switch (fault)
        {
            case "gap":
            case "tail":
            case "regression":
                var integrity = await Assert.ThrowsAsync<EventHistoryException>(() =>
                    ReadAsync(scope, inventory)
                );
                Assert.Equal(
                    fault switch
                    {
                        "gap" => EventHistoryFailure.UnexpectedVersion,
                        "tail" => EventHistoryFailure.RangeMismatch,
                        _ => EventHistoryFailure.RecordedTimeRegression,
                    },
                    integrity.Failure
                );
                break;
            case "unknown":
            case "payload":
                await Assert.ThrowsAsync<EventDecodingException>(() => ReadAsync(scope, inventory));
                break;
            case "sequence":
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    ReadAsync(scope, inventory)
                );
                break;
            default:
                await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(scope, inventory));
                break;
        }
        if (fault != "creation")
        {
            long earlier = fault == "gap" ? 1 : 2;
            Assert.NotNull(await ReadAsync(scope, inventory, version: earlier));
        }
        if (fault is "tail" or "update")
        {
            // At/after header update time targets the head; missing rows cannot disguise corruption.
            if (fault == "tail")
                await Assert.ThrowsAsync<EventHistoryException>(() =>
                    ReadAsync(scope, inventory, cutoff: FixtureHistories.OpenedAt.AddMinutes(1))
                );
            else
                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    ReadAsync(scope, inventory, cutoff: FixtureHistories.OpenedAt.AddMinutes(1))
                );
        }
        await using var beta = Scope(provider, Beta);
        Assert.Equal((3, inventory ? 26m : 125m), Amount(await ReadAsync(beta, inventory)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingCreationWithinTheStreamLifetimeIsNotABeforeFirstResult(bool inventory)
    {
        string connection = await SeedAsync();
        await ExecuteAsync(
            connection,
            $"DELETE FROM {Schema(inventory)}.events WHERE organization_key = @owner"
        );
        await using var provider = Provider(connection);
        await using var scope = Scope(provider, Alpha);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ReadAsync(scope, inventory, cutoff: FixtureHistories.OpenedAt.AddSeconds(5))
        );
    }

    [Theory]
    [InlineData(true, "foreign-stream", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData(false, "foreign-stream", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData(true, "duplicate-position", PostgresErrorCodes.UniqueViolation)]
    [InlineData(false, "duplicate-position", PostgresErrorCodes.UniqueViolation)]
    [InlineData(true, "zero-head", PostgresErrorCodes.CheckViolation)]
    [InlineData(false, "zero-head", PostgresErrorCodes.CheckViolation)]
    [InlineData(true, "zero-position", PostgresErrorCodes.CheckViolation)]
    [InlineData(false, "zero-position", PostgresErrorCodes.CheckViolation)]
    [InlineData(true, "zero-schema", PostgresErrorCodes.CheckViolation)]
    [InlineData(false, "zero-schema", PostgresErrorCodes.CheckViolation)]
    public async Task ActualMigrationsEnforceOwnedStreamRelationshipsAndPositivePositions(
        bool inventory,
        string fault,
        string sqlState
    )
    {
        string connection = await SeedAsync();
        string schema = Schema(inventory);
        string statement = fault switch
        {
            "foreign-stream" =>
                $"INSERT INTO {schema}.events (organization_key, event_id, stream_id, stream_version, event_name, schema_version, recorded_at, payload) SELECT 'foreign-owner', gen_random_uuid(), stream_id, stream_version, event_name, schema_version, recorded_at, payload FROM {schema}.events WHERE organization_key = @owner AND stream_version = 1",
            "duplicate-position" =>
                $"INSERT INTO {schema}.events (organization_key, event_id, stream_id, stream_version, event_name, schema_version, recorded_at, payload) SELECT organization_key, gen_random_uuid(), stream_id, stream_version, event_name, schema_version, recorded_at, payload FROM {schema}.events WHERE organization_key = @owner AND stream_version = 1",
            "zero-head" =>
                $"UPDATE {schema}.event_streams SET version = 0 WHERE organization_key = @owner",
            "zero-position" =>
                $"UPDATE {schema}.events SET stream_version = 0 WHERE organization_key = @owner AND stream_version = 1",
            "zero-schema" =>
                $"UPDATE {schema}.events SET schema_version = 0 WHERE organization_key = @owner AND stream_version = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, statement)
        );
        Assert.Equal(sqlState, failure.SqlState);
        await using var provider = Provider(connection);
        await using var scope = Scope(provider, Alpha);
        Assert.Equal((3, inventory ? 13m : 62.50m), Amount(await ReadAsync(scope, inventory)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeQueryFailuresPropagateAndAFreshReadRecovers(bool inventory)
    {
        string connection = await SeedAsync();
        string schema = Schema(inventory);
        await using var provider = Provider(connection);
        await using (var scope = Scope(provider, Alpha))
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            if (inventory)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    scope
                        .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                        .ReadCurrentAsync(FixtureHistories.StreamId, cancellation.Token)
                );
            else
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    scope
                        .ServiceProvider.GetRequiredService<IPurchaseOrderHistory>()
                        .ReadCurrentAsync(FixtureHistories.StreamId, cancellation.Token)
                );
        }
        await ExecuteAsync(connection, $"ALTER TABLE {schema}.events RENAME TO unavailable_events");
        await using (var scope = Scope(provider, Alpha))
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                ReadAsync(scope, inventory)
            );
            Assert.Equal(PostgresErrorCodes.UndefinedTable, failure.SqlState);
        }
        await ExecuteAsync(connection, $"ALTER TABLE {schema}.unavailable_events RENAME TO events");
        await using var fresh = Scope(provider, Alpha);
        Assert.Equal((3, inventory ? 13m : 62.50m), Amount(await ReadAsync(fresh, inventory)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForwardInventoryMigrationPreservesCatalogAndModulesCanMigrateInEitherOrder(
        bool purchasingFirst
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using (var setup = provider.CreateAsyncScope())
        {
            if (purchasingFirst)
                await setup
                    .ServiceProvider.GetRequiredService<PurchasingDbContext>()
                    .Database.MigrateAsync(Token);
            var inventory = setup.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await inventory
                .GetService<IMigrator>()
                .MigrateAsync("20261005214919_InitialInventory", Token);
        }
        await using (var old = Scope(provider, Alpha))
        {
            var inventory = old.ServiceProvider.GetRequiredService<InventoryDbContext>();
            InventoryDemoSeed.Stage(inventory, 42);
            await inventory.SaveChangesAsync(Token);
        }
        await using (var setup = provider.CreateAsyncScope())
        {
            await setup
                .ServiceProvider.GetRequiredService<InventoryDbContext>()
                .Database.MigrateAsync(Token);
            await setup
                .ServiceProvider.GetRequiredService<PurchasingDbContext>()
                .Database.MigrateAsync(Token);
        }
        await using var scope = Scope(provider, Alpha);
        var catalog = scope.ServiceProvider.GetRequiredService<IStockCatalog>();
        Assert.Equal(42, (await catalog.ReadAsync("DEMO-NOTEBOOK", Token))!.AvailableQuantity);
        var inventoryDatabase = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var purchasingDatabase = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
        await using var inventoryTransaction =
            await inventoryDatabase.Database.BeginTransactionAsync(Token);
        await using var purchasingTransaction =
            await purchasingDatabase.Database.BeginTransactionAsync(Token);
        InventoryHistorySeed.Add(
            inventoryDatabase,
            FixtureHistories.StreamId,
            FixtureHistories.Inventory(Fixtures)
        );
        PurchasingHistorySeed.Add(
            purchasingDatabase,
            FixtureHistories.StreamId,
            FixtureHistories.Purchasing(Fixtures)
        );
        await inventoryDatabase.SaveChangesAsync(Token);
        await purchasingDatabase.SaveChangesAsync(Token);
        await inventoryTransaction.CommitAsync(Token);
        await purchasingTransaction.CommitAsync(Token);
        Assert.Equal((3, 13m), Amount(await ReadAsync(scope, true)));
        Assert.Equal((3, 62.50m), Amount(await ReadAsync(scope, false)));
        Assert.Equal(42, (await catalog.ReadAsync("DEMO-NOTEBOOK", Token))!.AvailableQuantity);
        Assert.Empty(await inventoryDatabase.Database.GetPendingMigrationsAsync(Token));
        Assert.Empty(await purchasingDatabase.Database.GetPendingMigrationsAsync(Token));
    }

    [Fact]
    public async Task NativeExecutableUsesBothModulesAndBothTenants()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(DemoJourneys).Assembly.Location);
        start.Environment["WHOLESALE_DEMO_CONNECTION_STRING"] = connection;
        using var process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(Token);
        Task<string> error = process.StandardError.ReadToEndAsync(Token);
        await process.WaitForExitAsync(Token);
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(await error);
        string[] expected =
        [
            "wholesale-alpha: stock current=13.000, version-2=10.125, cutoff=10.125, before-open=none",
            "wholesale-alpha: order current=62.50, version-2=31.25, cutoff=62.50, before-draft=none",
            "wholesale-beta: stock current=26.000, version-2=20.250, cutoff=20.250, before-open=none",
            "wholesale-beta: order current=125.00, version-2=62.50, cutoff=125.00, before-draft=none",
            "inventory append: committed-version=3, on-hand=13.000",
            "purchasing append: committed-version=3, total=62.50",
            "inventory inline: committed-version=3, on-hand=13.000",
            "purchasing inline: committed-version=3, total=62.50",
            "purchasing summary: committed-version=3, lines=1, total=62.50",
            "inventory issues: committed-version=5, on-hand=6.000, rejected=7",
            "inventory rebuilt: version=1",
            "purchasing rebuilt: version=1",
        ];
        Assert.Equal(
            expected,
            (await output).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        );
    }

    private async Task<string> SeedAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using (var setup = provider.CreateAsyncScope())
        {
            await setup
                .ServiceProvider.GetRequiredService<InventoryDbContext>()
                .Database.MigrateAsync(Token);
            await setup
                .ServiceProvider.GetRequiredService<PurchasingDbContext>()
                .Database.MigrateAsync(Token);
        }
        foreach (var (owner, factor) in new[] { (Alpha, 1m), (Beta, 2m) })
        {
            await using var scope = Scope(provider, owner);
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var inventoryTransaction = await inventory.Database.BeginTransactionAsync(
                Token
            );
            InventoryHistorySeed.Add(
                inventory,
                FixtureHistories.StreamId,
                FixtureHistories.Inventory(Fixtures, factor)
            );
            await inventory.SaveChangesAsync(Token);
            await inventoryTransaction.CommitAsync(Token);
            var purchasing = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
            await using var purchasingTransaction = await purchasing.Database.BeginTransactionAsync(
                Token
            );
            PurchasingHistorySeed.Add(
                purchasing,
                FixtureHistories.StreamId,
                FixtureHistories.Purchasing(Fixtures, factor)
            );
            await purchasing.SaveChangesAsync(Token);
            await purchasingTransaction.CommitAsync(Token);
        }
        return connection;
    }

    private static ServiceProvider Provider(string connection, QueryObservation? observation = null)
    {
        var services = DemoComposition.CreateServices(connection);
        services.AddInventoryQueries();
        if (observation is not null)
        {
            services.AddDbContext<InventoryDbContext>(options =>
                options.AddInterceptors(observation)
            );
            services.AddDbContext<PurchasingDbContext>(options =>
                options.AddInterceptors(observation)
            );
        }
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private static AsyncServiceScope Scope(ServiceProvider provider, string owner)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(owner)));
        return scope;
    }

    private static DbContext Context(AsyncServiceScope scope, bool inventory) =>
        inventory
            ? scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            : scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();

    private static string Schema(bool inventory) => inventory ? "inventory" : "purchasing";

    private static async Task<(long Version, decimal Amount)?> ReadAsync(
        AsyncServiceScope scope,
        bool inventory,
        long? version = null,
        DateTimeOffset? cutoff = null,
        Guid? id = null
    )
    {
        Guid streamId = id ?? FixtureHistories.StreamId;
        if (inventory)
        {
            var query = scope.ServiceProvider.GetRequiredService<IStockPositionHistory>();
            var result =
                version is { } v ? await query.ReadAtVersionAsync(streamId, v, Token)
                : cutoff is { } at ? await query.ReadAsOfAsync(streamId, at, Token)
                : await query.ReadCurrentAsync(streamId, Token);
            return result is null ? null : (result.Version, result.OnHand);
        }
        else
        {
            var query = scope.ServiceProvider.GetRequiredService<IPurchaseOrderHistory>();
            var result =
                version is { } v ? await query.ReadAtVersionAsync(streamId, v, Token)
                : cutoff is { } at ? await query.ReadAsOfAsync(streamId, at, Token)
                : await query.ReadCurrentAsync(streamId, Token);
            return result is null ? null : (result.Version, result.Total);
        }
    }

    private static (long Version, decimal Amount) Amount((long Version, decimal Amount)? result) =>
        result ?? throw new InvalidOperationException("Expected a reconstructed history.");

    private static async Task ExecuteAsync(
        string connection,
        string sql,
        DateTimeOffset? time = null
    )
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        command.Parameters.AddWithValue("owner", Alpha);
        if (time is { } value)
            command.Parameters.AddWithValue("time", value);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task AppendFourthFixtureAsync(
        string connection,
        bool inventory,
        CancellationToken cancellationToken
    )
    {
        string schema = Schema(inventory);
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(cancellationToken);
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO {schema}.events (organization_key, event_id, stream_id, stream_version, event_name, schema_version, recorded_at, payload)
            SELECT organization_key, gen_random_uuid(), stream_id, 4, event_name, schema_version, @time,
                jsonb_set(payload, ARRAY['quantity'], @quantity::jsonb)
            FROM {schema}.events WHERE organization_key = @owner AND stream_version = 3;
            UPDATE {schema}.event_streams SET version = 4, updated_at = @time WHERE organization_key = @owner;
            """,
            database,
            transaction
        );
        command.Parameters.AddWithValue("owner", Alpha);
        command.Parameters.AddWithValue("time", FixtureHistories.OpenedAt.AddSeconds(30));
        command.Parameters.AddWithValue("quantity", inventory ? "5" : "7");
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
