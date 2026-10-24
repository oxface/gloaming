using Gloaming.Events.History;
using Gloaming.Events.Serialization;
using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Tenancy;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.EventPersistence;

public sealed class HistoryReadTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string Alpha = "wholesale-alpha";
    private const string Beta = "wholesale-beta";
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InventoryReconstructsSelectedRangesWithinTheCurrentTenant()
    {
        string connection = await SeedAsync();
        var observation = new QueryObservation(Schema);
        await using var provider = Provider(connection, observation);
        foreach (var (owner, factor) in new[] { (Alpha, 1m), (Beta, 2m) })
        {
            await using var scope = Scope(provider, owner);
            Assert.Equal((3, (13m) * factor), Amount(await ReadAsync(scope)));
            Assert.Equal((2, (10.125m) * factor), Amount(await ReadAsync(scope, version: 2)));
            Assert.Equal(
                (2, (10.125m) * factor),
                Amount(
                    await ReadAsync(
                        scope,
                        cutoff: FixtureHistories.FirstChangeAt.ToOffset(TimeSpan.FromHours(2))
                    )
                )
            );
            Assert.Equal(
                (1, 0m),
                Amount(await ReadAsync(scope, cutoff: FixtureHistories.OpenedAt.AddSeconds(5)))
            );
            Assert.Null(await ReadAsync(scope, cutoff: FixtureHistories.OpenedAt.AddTicks(-1)));
            Assert.Null(await ReadAsync(scope, id: Guid.NewGuid()));
            Assert.Equal((3, (13m) * factor), Amount(await ReadAsync(scope)));
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

            Assert.Empty(Context(scope).ChangeTracker.Entries());
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

    [Fact]
    public async Task ACommitAfterHeadCaptureIsExcludedUntilAFreshRead()
    {
        string connection = await SeedAsync();
        var observation = new QueryObservation(Schema)
        {
            BeforeEventRead = cancellation => AppendFourthFixtureAsync(connection, cancellation),
        };
        await using var provider = Provider(connection, observation);
        await using (var scope = Scope(provider, Alpha))
            Assert.Equal((3, 13m), Amount(await ReadAsync(scope)));
        await using var fresh = Scope(provider, Alpha);
        Assert.Equal((4, 18m), Amount(await ReadAsync(fresh)));
    }

    [Fact]
    public async Task MissingAndTenantlessContextRejectReadsAndFutureVersionsAreNotClamped()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var missing = provider.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(missing));
        await using (var tenantless = provider.CreateAsyncScope())
        {
            tenantless
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
            await Assert.ThrowsAsync<TenantRequiredException>(() => ReadAsync(tenantless));
        }

        await using var alpha = Scope(provider, Alpha);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ReadAsync(alpha, version: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ReadAsync(alpha, version: 4));
        await using var stranger = Scope(provider, "foreign-owner");
        Assert.Null(await ReadAsync(stranger));
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("tail")]
    [InlineData("regression")]
    [InlineData("unknown")]
    [InlineData("payload")]
    [InlineData("creation")]
    [InlineData("update")]
    [InlineData("sequence")]
    public async Task DamagedSelectedHistoryFailsAtItsOwnBoundary(string fault)
    {
        string connection = await SeedAsync();
        string schema = Schema;
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
                    ReadAsync(scope)
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
                await Assert.ThrowsAsync<EventDecodingException>(() => ReadAsync(scope));
                break;
            case "sequence":
                await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(scope));
                break;
            default:
                await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(scope));
                break;
        }

        if (fault != "creation")
        {
            long earlier = fault == "gap" ? 1 : 2;
            Assert.NotNull(await ReadAsync(scope, version: earlier));
        }

        if (fault is "tail" or "update")
        {
            // At/after header update time targets the head; missing rows cannot disguise corruption.
            if (fault == "tail")
                await Assert.ThrowsAsync<EventHistoryException>(() =>
                    ReadAsync(scope, cutoff: FixtureHistories.OpenedAt.AddMinutes(1))
                );
            else
                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    ReadAsync(scope, cutoff: FixtureHistories.OpenedAt.AddMinutes(1))
                );
        }

        await using var beta = Scope(provider, Beta);
        Assert.Equal((3, 26m), Amount(await ReadAsync(beta)));
    }

    [Fact]
    public async Task MissingCreationWithinTheStreamLifetimeIsNotABeforeFirstResult()
    {
        string connection = await SeedAsync();
        await ExecuteAsync(
            connection,
            $"DELETE FROM {Schema}.events WHERE organization_key = @owner"
        );
        await using var provider = Provider(connection);
        await using var scope = Scope(provider, Alpha);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ReadAsync(scope, cutoff: FixtureHistories.OpenedAt.AddSeconds(5))
        );
    }

    [Theory]
    [InlineData("foreign-stream", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("duplicate-position", PostgresErrorCodes.UniqueViolation)]
    [InlineData("zero-head", PostgresErrorCodes.CheckViolation)]
    [InlineData("zero-position", PostgresErrorCodes.CheckViolation)]
    [InlineData("zero-schema", PostgresErrorCodes.CheckViolation)]
    public async Task ActualMigrationsEnforceOwnedStreamRelationshipsAndPositivePositions(
        string fault,
        string sqlState
    )
    {
        string connection = await SeedAsync();
        string schema = Schema;
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
        Assert.Equal((3, 13m), Amount(await ReadAsync(scope)));
    }

    [Fact]
    public async Task NativeQueryFailuresPropagateAndAFreshReadRecovers()
    {
        string connection = await SeedAsync();
        string schema = Schema;
        await using var provider = Provider(connection);
        await using (var scope = Scope(provider, Alpha))
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                    .ReadCurrentAsync(FixtureHistories.StreamId, cancellation.Token)
            );
        }

        await ExecuteAsync(connection, $"ALTER TABLE {schema}.events RENAME TO unavailable_events");
        await using (var scope = Scope(provider, Alpha))
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(scope));
            Assert.Equal(PostgresErrorCodes.UndefinedTable, failure.SqlState);
        }

        await ExecuteAsync(connection, $"ALTER TABLE {schema}.unavailable_events RENAME TO events");
        await using var fresh = Scope(provider, Alpha);
        Assert.Equal((3, 13m), Amount(await ReadAsync(fresh)));
    }

    [Fact]
    public async Task ForwardInventoryMigrationPreservesCatalogAndAddsOwnedHistory()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using (var setup = provider.CreateAsyncScope())
        {
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
            await setup
                .ServiceProvider.GetRequiredService<InventoryDbContext>()
                .Database.MigrateAsync(Token);

        await using var scope = Scope(provider, Alpha);
        var catalog = scope.ServiceProvider.GetRequiredService<IStockCatalog>();
        Assert.Equal(42, (await catalog.ReadAsync("DEMO-NOTEBOOK", Token))!.AvailableQuantity);
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        InventoryHistorySeed.Add(
            database,
            FixtureHistories.StreamId,
            FixtureHistories.Inventory(Fixtures)
        );
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal((3, 13m), Amount(await ReadAsync(scope)));
        Assert.Equal(42, (await catalog.ReadAsync("DEMO-NOTEBOOK", Token))!.AvailableQuantity);
        Assert.Empty(await database.Database.GetPendingMigrationsAsync(Token));
    }

    [Fact]
    public async Task NativeExecutableUsesInventoryAcrossBothTenants()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var process = new ChildHost(
            typeof(DemoJourneys).Assembly,
            new() { ["WHOLESALE_SCENARIO_CONNECTION"] = connection },
            "--scenario",
            "history"
        );
        Assert.True(await process.WaitForExitAsync(deadline.Token) == 0, process.Output);
        string[] expected =
        [
            "wholesale-alpha: stock current=13.000, version-2=10.125, cutoff=10.125, before-open=none",
            "wholesale-beta: stock current=26.000, version-2=20.250, cutoff=20.250, before-open=none",
            "inventory append: committed-version=3, on-hand=13.000",
            "inventory inline: committed-version=3, on-hand=13.000",
            "inventory issues: committed-version=5, on-hand=6.000, rejected=7",
            "inventory rebuilt: version=1",
        ];
        Assert.Equal(
            expected,
            process.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
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

    private static InventoryDbContext Context(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

    private const string Schema = "inventory";

    private static async Task<(long Version, decimal Amount)?> ReadAsync(
        AsyncServiceScope scope,
        long? version = null,
        DateTimeOffset? cutoff = null,
        Guid? id = null
    )
    {
        Guid streamId = id ?? FixtureHistories.StreamId;
        var query = scope.ServiceProvider.GetRequiredService<IStockPositionHistory>();
        var result =
            version is { } v ? await query.ReadAtVersionAsync(streamId, v, Token)
            : cutoff is { } at ? await query.ReadAsOfAsync(streamId, at, Token)
            : await query.ReadCurrentAsync(streamId, Token);
        return result is null ? null : (result.Version, result.OnHand);
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
        CancellationToken cancellationToken
    )
    {
        string schema = Schema;
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
        command.Parameters.AddWithValue("quantity", "5");
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
