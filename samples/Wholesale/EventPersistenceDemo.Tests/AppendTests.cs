using Gloaming.Events.History;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo.Tests;

public sealed partial class AppendTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private const string Alpha = "append-alpha";
    private const string Beta = "append-beta";
    private static readonly Guid Id = Guid.Parse("e5220000-0000-0000-0000-000000000001");
    private static readonly decimal[] InitialBatch = [2m, 3m];
    private static readonly decimal[] WinningBatch = [7m, 8m];
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static DateTimeOffset Opened => FixtureHistories.OpenedAt;
    private static DateTimeOffset Changed => FixtureHistories.FirstChangeAt;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreationAndBatchAreInvisibleUntilTheCallerCommits(bool inventory)
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((1, 0m), Proposed(await CreateAsync(write, inventory)));
            await AssertReadAsync(provider, inventory, null);
            await database.SaveChangesAsync(Token);
            await AssertReadAsync(provider, inventory, null);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (1, 0m));
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                (3, inventory ? 5m : 37.5m),
                Proposed(await AppendAsync(write, inventory, 1, InitialBatch))
            );
            await AssertReadAsync(provider, inventory, (1, 0m));
            await database.SaveChangesAsync(Token);
            await AssertReadAsync(provider, inventory, (1, 0m));
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        Assert.Equal(3, await EventCountAsync(connection, inventory));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task CompetingWritersCommitOneCompleteWinner(bool inventory, bool creation)
    {
        string connection = creation ? await DatabaseAsync() : await SeedAsync(inventory);
        var observation = new QueryObservation(Schema(inventory));
        await using var provider = Provider(connection, observation);
        await using var winner = Scope(provider, Alpha);
        await using var loser = Scope(provider, Alpha);
        var winnerDatabase = Context(winner, inventory);
        var loserDatabase = Context(loser, inventory);
        await using var winnerTransaction = await winnerDatabase.Database.BeginTransactionAsync(
            Token
        );
        await using var loserTransaction = await loserDatabase.Database.BeginTransactionAsync(
            Token
        );
        Assert.Equal(
            creation ? (1L, 0m) : (5L, inventory ? 20m : 100m),
            creation
                ? Proposed(await CreateAsync(winner, inventory))
                : Proposed(await AppendAsync(winner, inventory, 3, WinningBatch))
        );
        Assert.Equal(
            creation ? (1L, 0m) : (5L, inventory ? 10m : 37.5m),
            creation
                ? Proposed(await CreateAsync(loser, inventory))
                : Proposed(await AppendAsync(loser, inventory, 3, InitialBatch))
        );
        await winnerDatabase.SaveChangesAsync(Token);
        await winnerTransaction.CommitAsync(Token);
        var failure = await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            loserDatabase.SaveChangesAsync(Token)
        );
        Assert.True(IsConflict(inventory, failure));
        await loserTransaction.RollbackAsync(CancellationToken.None);
        await AssertReadAsync(
            provider,
            inventory,
            creation ? (1, 0m) : (5, inventory ? 20m : 100m)
        );
        Assert.Equal(creation ? 1 : 5, await EventCountAsync(connection, inventory));
        if (!creation)
        {
            var update = observation.Commands.First(command =>
                command.Sql.Contains(
                    $"UPDATE {Schema(inventory)}.event_streams",
                    StringComparison.Ordinal
                )
            );
            Assert.Contains(
                "version =",
                update.Sql[update.Sql.IndexOf("WHERE", StringComparison.Ordinal)..],
                StringComparison.Ordinal
            );
            Assert.Contains(3L, update.Values);
            Assert.Contains(Alpha, update.Values);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TenantIsolationAndPreflightOutcomesProtectOwnedHistory(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal("conflict", Status(await CreateAsync(write, inventory)));
            Assert.Equal("conflict", Status(await AppendAsync(write, inventory, 2, InitialBatch)));
            Assert.Equal("conflict", Status(await AppendAsync(write, inventory, 4, InitialBatch)));
            Assert.Empty(database.ChangeTracker.Entries());
        }
        await using (var write = Scope(provider, Beta))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal("not-found", Status(await AppendAsync(write, inventory, 3, InitialBatch)));
            Assert.Equal((1, 0m), Proposed(await CreateAsync(write, inventory)));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        await AssertReadAsync(provider, inventory, (1, 0m), Beta);
    }

    [Theory]
    [InlineData(true, "missing")]
    [InlineData(false, "missing")]
    [InlineData(true, "tenantless")]
    [InlineData(false, "tenantless")]
    [InlineData(true, "transaction")]
    [InlineData(false, "transaction")]
    [InlineData(true, "empty")]
    [InlineData(false, "empty")]
    [InlineData(true, "quantity")]
    [InlineData(false, "quantity")]
    [InlineData(true, "negative-version")]
    [InlineData(false, "negative-version")]
    [InlineData(true, "creation-version")]
    [InlineData(false, "creation-version")]
    [InlineData(true, "regression")]
    [InlineData(false, "regression")]
    [InlineData(true, "non-utc")]
    [InlineData(false, "non-utc")]
    public async Task InvalidWriteCannotTrackChanges(bool inventory, string fault)
    {
        string connection = await SeedAsync(inventory);
        await using var provider = Provider(connection);
        await using var write = fault is "missing" or "tenantless"
            ? provider.CreateAsyncScope()
            : Scope(provider, Alpha);
        if (fault == "tenantless")
            write
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
        var database = Context(write, inventory);
        await using var transaction =
            fault == "transaction" ? null : await database.Database.BeginTransactionAsync(Token);
        Task<object> action() =>
            fault == "creation-version"
                ? CreateAsync(write, inventory, expected: 1)
                : AppendAsync(
                    write,
                    inventory,
                    fault == "negative-version" ? -1 : 3,
                    fault == "empty" ? []
                        : fault == "quantity" ? [0]
                        : InitialBatch,
                    time: fault == "regression" ? Opened
                        : fault == "non-utc" ? Changed.ToOffset(TimeSpan.FromHours(2))
                        : Changed
                );
        if (fault == "tenantless")
            await Assert.ThrowsAsync<TenantRequiredException>(action);
        else if (fault is "missing" or "transaction" or "regression" or "non-utc")
            await Assert.ThrowsAsync<InvalidOperationException>(action);
        else
            await Assert.ThrowsAnyAsync<ArgumentException>(action);
        Assert.Empty(database.ChangeTracker.Entries());
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedWorkCanBeCancelledAndRepeatedStagingRequiresAFreshContext(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, inventory, 3, WinningBatch));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AppendAsync(write, inventory, 5, InitialBatch)
            );
            await database.SaveChangesAsync(Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AppendAsync(write, inventory, 5, InitialBatch)
            );
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                transaction.CommitAsync(cancellation.Token)
            );
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        await using (var fresh = Scope(provider, Alpha))
        {
            var database = Context(fresh, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(fresh, inventory, 3, WinningBatch));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (5, inventory ? 20m : 100m));
    }

    [Theory]
    [InlineData(true, "header")]
    [InlineData(false, "header")]
    [InlineData(true, "first-event")]
    [InlineData(false, "first-event")]
    [InlineData(true, "later-event")]
    [InlineData(false, "later-event")]
    [InlineData(true, "event-identity")]
    [InlineData(false, "event-identity")]
    [InlineData(true, "position")]
    [InlineData(false, "position")]
    [InlineData(true, "current")]
    [InlineData(false, "current")]
    public async Task WriteFaultsRollBackTheWholeBatchAndClassificationIsNarrow(
        bool inventory,
        string fault
    )
    {
        string connection = await SeedAsync(inventory);
        string schema = Schema(inventory);
        await FaultAsync(connection, inventory, fault);
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, inventory, 3, WinningBatch));
            var failure = await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                database.SaveChangesAsync(Token)
            );
            Assert.Equal(fault == "position", IsConflict(inventory, failure));
            var postgresFailure = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(
                fault is "event-identity" or "position"
                    ? PostgresErrorCodes.UniqueViolation
                    : PostgresErrorCodes.CheckViolation,
                postgresFailure.SqlState
            );
            if (fault == "event-identity")
                Assert.Equal("PK_events", postgresFailure.ConstraintName);
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        Assert.Equal(3, await EventCountAsync(connection, inventory));
        await ExecuteAsync(
            connection,
            fault == "header"
                    ? $"ALTER TABLE {schema}.event_streams DROP CONSTRAINT test_header_fault"
                : fault is "first-event" or "later-event"
                    ? $"ALTER TABLE {schema}.events DROP CONSTRAINT test_event_fault"
                : fault == "current"
                    ? $"ALTER TABLE {schema}.{CurrentTable(inventory)} DROP CONSTRAINT test_view_fault"
                : $"DROP TRIGGER test_fault ON {schema}.events; DROP FUNCTION {schema}.test_fault()"
        );
        await using var fresh = Scope(provider, Alpha);
        var freshDatabase = Context(fresh, inventory);
        await using var freshTransaction = await freshDatabase.Database.BeginTransactionAsync(
            Token
        );
        Proposed(await AppendAsync(fresh, inventory, 3, WinningBatch));
        await freshDatabase.SaveChangesAsync(Token);
        await freshTransaction.CommitAsync(Token);
        await AssertReadAsync(provider, inventory, (5, inventory ? 20m : 100m));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASecondFailingSaveRollsBackTheFirstStreamToo(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        string schema = Schema(inventory);
        await using var provider = Provider(connection);
        Guid secondId = Guid.NewGuid();
        await ExecuteAsync(
            connection,
            $"ALTER TABLE {schema}.events ADD CONSTRAINT test_event_fault CHECK (stream_version <> 1) NOT VALID"
        );
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, inventory, 3, WinningBatch));
            await database.SaveChangesAsync(Token);
            Proposed(await CreateAsync(write, inventory, secondId));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                database.SaveChangesAsync(Token)
            );
            Assert.False(IsConflict(inventory, failure));
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        await AssertReadAsync(provider, inventory, null, id: secondId);
        Assert.Equal(3, await EventCountAsync(connection, inventory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InlineCommandsDoNotCertifyHistoryThatOnlyLiveReadsInspect(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        await ExecuteAsync(
            connection,
            $"DELETE FROM {Schema(inventory)}.events WHERE stream_version = 2"
        );
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, inventory, 3, WinningBatch));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using var read = Scope(provider, Alpha);
        Assert.Equal((5, inventory ? 20m : 100m), await CurrentAsync(read, inventory));
        await Assert.ThrowsAsync<EventHistoryException>(async () =>
        {
            if (inventory)
                await read
                    .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                    .ReadCurrentAsync(Id, Token);
            else
                await read
                    .ServiceProvider.GetRequiredService<IPurchaseOrderHistory>()
                    .ReadCurrentAsync(Id, Token);
        });
        Assert.Equal(4, await EventCountAsync(connection, inventory));
    }

    [Fact]
    public async Task CatalogConcurrencyIsNotClassifiedAsAnEventAppendConflict()
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        await using var scope = Scope(provider, Alpha);
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        InventoryDemoSeed.Stage(database, 42);
        await database.SaveChangesAsync(Token);
        var row = Assert.Single(database.ChangeTracker.Entries());
        row.Property("AvailableQuantity").CurrentValue = 43;
        await ExecuteAsync(connection, "DELETE FROM inventory.stock_availability");
        var failure = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            database.SaveChangesAsync(Token)
        );
        Assert.False(InventoryAppendFailures.IsVersionConflict(failure));
    }

    private async Task<string> DatabaseAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using var setup = provider.CreateAsyncScope();
        await setup
            .ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Database.MigrateAsync(Token);
        await setup
            .ServiceProvider.GetRequiredService<PurchasingDbContext>()
            .Database.MigrateAsync(Token);
        return connection;
    }

    private async Task<string> SeedAsync(bool inventory)
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        await using (var create = Scope(provider, Alpha))
        {
            var database = Context(create, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await CreateAsync(create, inventory));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using (var append = Scope(provider, Alpha))
        {
            var database = Context(append, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(append, inventory, 1, InitialBatch));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        return connection;
    }

    private static ServiceProvider Provider(string connection, QueryObservation? observation = null)
    {
        var services = DemoComposition.CreateServices(connection);
        services.AddScoped<TestClock>();
        services.AddScoped<TimeProvider>(provider => provider.GetRequiredService<TestClock>());
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

    private static bool IsConflict(bool inventory, DbUpdateException failure) =>
        inventory
            ? InventoryAppendFailures.IsVersionConflict(failure)
            : PurchasingAppendFailures.IsVersionConflict(failure);

    private static async Task<object> CreateAsync(
        AsyncServiceScope scope,
        bool inventory,
        Guid? id = null,
        long expected = 0
    )
    {
        Guid streamId = id ?? Id;
        if (inventory)
            return await scope
                .ServiceProvider.WithClock(Opened)
                .GetRequiredService<IStockPositionCommands>()
                .OpenAsync(
                    new OpenStockPosition(
                        streamId,
                        Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        "EA",
                        expected
                    ),
                    Token
                );
        return await scope
            .ServiceProvider.WithClock(Opened)
            .GetRequiredService<IPurchaseOrderCommands>()
            .DraftAsync(
                new DraftPurchaseOrder(streamId, "APPEND-1", "SUP-1", "EUR", expected),
                Token
            );
    }

    private static async Task<object> AppendAsync(
        AsyncServiceScope scope,
        bool inventory,
        long expected,
        decimal[] quantities,
        DateTimeOffset? time = null
    )
    {
        if (inventory)
            return await scope
                .ServiceProvider.WithClock(time ?? Changed)
                .GetRequiredService<IStockPositionCommands>()
                .ReceiveAsync(
                    new ReceiveStock(
                        Id,
                        expected,
                        quantities.Select(quantity => new StockReceipt(quantity)).ToArray()
                    ),
                    Token
                );
        return await scope
            .ServiceProvider.WithClock(time ?? Changed)
            .GetRequiredService<IPurchaseOrderCommands>()
            .ChangeLinesAsync(
                new ChangePurchaseOrderLines(
                    Id,
                    expected,
                    quantities
                        .Select(quantity => new PurchaseOrderLine("ITEM-1", quantity, 12.5m))
                        .ToArray()
                ),
                Token
            );
    }

    private static (long Version, decimal Amount) Proposed(object result) =>
        result switch
        {
            StockPositionChangeResult.Changed staged => (
                staged.Proposed.Version,
                staged.Proposed.OnHand
            ),
            PurchaseOrderChangeResult.Changed staged => (
                staged.Proposed.Version,
                staged.Proposed.Total
            ),
            _ => throw new InvalidOperationException("Expected a staged business proposal."),
        };

    private static string Status(object result) =>
        result switch
        {
            StockPositionChangeResult.NotFound or PurchaseOrderChangeResult.NotFound => "not-found",
            StockPositionChangeResult.Conflict or PurchaseOrderChangeResult.Conflict => "conflict",
            _ => throw new InvalidOperationException("Expected a preflight rejection."),
        };

    private static async Task AssertReadAsync(
        ServiceProvider provider,
        bool inventory,
        (long Version, decimal Amount)? expected,
        string owner = Alpha,
        Guid? id = null
    )
    {
        await using var read = Scope(provider, owner);
        Guid streamId = id ?? Id;
        (long Version, decimal Amount)? actual;
        if (inventory)
        {
            var state = await read
                .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                .ReadCurrentAsync(streamId, Token);
            actual = state is null ? null : (state.Version, state.OnHand);
        }
        else
        {
            var state = await read
                .ServiceProvider.GetRequiredService<IPurchaseOrderHistory>()
                .ReadCurrentAsync(streamId, Token);
            actual = state is null ? null : (state.Version, state.Total);
        }
        Assert.Equal(expected, actual);
        Assert.Equal(expected, await CurrentAsync(read, inventory, streamId));
        if (!inventory)
        {
            var summary = await read
                .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
                .ReadSummaryAsync(streamId, Token);
            Assert.Equal(
                expected,
                summary is null
                    ? null
                    : ((long Version, decimal Amount)?)(summary.Version, summary.Total)
            );
        }
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<long> EventCountAsync(string connection, bool inventory)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {Schema(inventory)}.events WHERE organization_key = @owner",
            database
        );
        command.Parameters.AddWithValue("owner", Alpha);
        return (long)(await command.ExecuteScalarAsync(Token))!;
    }

    private static Task FaultAsync(string connection, bool inventory, string fault)
    {
        string schema = Schema(inventory);
        string statement = fault switch
        {
            "current" =>
                $"ALTER TABLE {schema}.{CurrentTable(inventory)} ADD CONSTRAINT test_view_fault CHECK (version <= 3)",
            "header" =>
                $"ALTER TABLE {schema}.event_streams ADD CONSTRAINT test_header_fault CHECK (version <= 3)",
            "first-event" =>
                $"ALTER TABLE {schema}.events ADD CONSTRAINT test_event_fault CHECK (stream_version <= 3)",
            "later-event" =>
                $"ALTER TABLE {schema}.events ADD CONSTRAINT test_event_fault CHECK (stream_version <> 5)",
            "position" =>
                $"CREATE FUNCTION {schema}.test_fault() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN NEW.stream_version := 3; RETURN NEW; END'; CREATE TRIGGER test_fault BEFORE INSERT ON {schema}.events FOR EACH ROW EXECUTE FUNCTION {schema}.test_fault()",
            "event-identity" =>
                $"CREATE FUNCTION {schema}.test_fault() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN NEW.event_id := (SELECT event_id FROM {schema}.events WHERE organization_key = NEW.organization_key AND stream_version = 1); RETURN NEW; END'; CREATE TRIGGER test_fault BEFORE INSERT ON {schema}.events FOR EACH ROW EXECUTE FUNCTION {schema}.test_fault()",
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        return ExecuteAsync(connection, statement);
    }
}
