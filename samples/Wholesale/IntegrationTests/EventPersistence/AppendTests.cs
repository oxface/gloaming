using Gloaming.Events.History;
using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Tenancy;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.EventPersistence;

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

    [Fact]
    public async Task CreationAndBatchAreInvisibleUntilTheCallerCommits()
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((1, 0m), Proposed(await CreateAsync(write)));
            await AssertReadAsync(provider, null);
            await database.SaveChangesAsync(Token);
            await AssertReadAsync(provider, null);
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (1, 0m));
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((3, 5m), Proposed(await AppendAsync(write, 1, InitialBatch)));
            await AssertReadAsync(provider, (1, 0m));
            await database.SaveChangesAsync(Token);
            await AssertReadAsync(provider, (1, 0m));
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (3, 5m));
        Assert.Equal(3, await EventCountAsync(connection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompetingWritersCommitOneCompleteWinner(bool creation)
    {
        string connection = creation ? await DatabaseAsync() : await SeedAsync();
        var observation = new QueryObservation(Schema);
        await using var provider = Provider(connection, observation);
        await using var winner = Scope(provider, Alpha);
        await using var loser = Scope(provider, Alpha);
        var winnerDatabase = Context(winner);
        var loserDatabase = Context(loser);
        await using var winnerTransaction = await winnerDatabase.Database.BeginTransactionAsync(
            Token
        );
        await using var loserTransaction = await loserDatabase.Database.BeginTransactionAsync(
            Token
        );
        Assert.Equal(
            creation ? (1L, 0m) : (5L, 20m),
            creation
                ? Proposed(await CreateAsync(winner))
                : Proposed(await AppendAsync(winner, 3, WinningBatch))
        );
        Assert.Equal(
            creation ? (1L, 0m) : (5L, 10m),
            creation
                ? Proposed(await CreateAsync(loser))
                : Proposed(await AppendAsync(loser, 3, InitialBatch))
        );
        await winnerDatabase.SaveChangesAsync(Token);
        await winnerTransaction.CommitAsync(Token);
        var failure = await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            loserDatabase.SaveChangesAsync(Token)
        );
        Assert.True(IsConflict(failure));
        await loserTransaction.RollbackAsync(CancellationToken.None);
        await AssertReadAsync(provider, creation ? (1, 0m) : (5, 20m));
        Assert.Equal(creation ? 1 : 5, await EventCountAsync(connection));
        if (!creation)
        {
            var update = observation.Commands.First(command =>
                command.Sql.Contains($"UPDATE {Schema}.event_streams", StringComparison.Ordinal)
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

    [Fact]
    public async Task TenantIsolationAndPreflightOutcomesProtectOwnedHistory()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal("conflict", Status(await CreateAsync(write)));
            Assert.Equal("conflict", Status(await AppendAsync(write, 2, InitialBatch)));
            Assert.Equal("conflict", Status(await AppendAsync(write, 4, InitialBatch)));
            Assert.Empty(database.ChangeTracker.Entries());
        }

        await using (var write = Scope(provider, Beta))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal("not-found", Status(await AppendAsync(write, 3, InitialBatch)));
            Assert.Equal((1, 0m), Proposed(await CreateAsync(write)));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (3, 5m));
        await AssertReadAsync(provider, (1, 0m), Beta);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tenantless")]
    [InlineData("transaction")]
    [InlineData("empty")]
    [InlineData("quantity")]
    [InlineData("negative-version")]
    [InlineData("creation-version")]
    [InlineData("regression")]
    [InlineData("non-utc")]
    public async Task InvalidWriteCannotTrackChanges(string fault)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var write = fault is "missing" or "tenantless"
            ? provider.CreateAsyncScope()
            : Scope(provider, Alpha);
        if (fault == "tenantless")
            write
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
        var database = Context(write);
        await using var transaction =
            fault == "transaction" ? null : await database.Database.BeginTransactionAsync(Token);
        Task<StockPositionChangeResult> action() =>
            fault == "creation-version"
                ? CreateAsync(write, expected: 1)
                : AppendAsync(
                    write,
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
        await AssertReadAsync(provider, (3, 5m));
    }

    [Fact]
    public async Task SavedWorkCanBeCancelledAndRepeatedStagingRequiresAFreshContext()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, 3, WinningBatch));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AppendAsync(write, 5, InitialBatch)
            );
            await database.SaveChangesAsync(Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AppendAsync(write, 5, InitialBatch)
            );
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                transaction.CommitAsync(cancellation.Token)
            );
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await AssertReadAsync(provider, (3, 5m));
        await using (var fresh = Scope(provider, Alpha))
        {
            var database = Context(fresh);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(fresh, 3, WinningBatch));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (5, 20m));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("first-event")]
    [InlineData("later-event")]
    [InlineData("event-identity")]
    [InlineData("position")]
    [InlineData("current")]
    public async Task WriteFaultsRollBackTheWholeBatchAndClassificationIsNarrow(string fault)
    {
        string connection = await SeedAsync();
        string schema = Schema;
        await FaultAsync(connection, fault);
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, 3, WinningBatch));
            var failure = await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                database.SaveChangesAsync(Token)
            );
            Assert.Equal(fault == "position", IsConflict(failure));
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

        await AssertReadAsync(provider, (3, 5m));
        Assert.Equal(3, await EventCountAsync(connection));
        await ExecuteAsync(
            connection,
            fault == "header"
                    ? $"ALTER TABLE {schema}.event_streams DROP CONSTRAINT test_header_fault"
                : fault is "first-event" or "later-event"
                    ? $"ALTER TABLE {schema}.events DROP CONSTRAINT test_event_fault"
                : fault == "current"
                    ? $"ALTER TABLE {schema}.{CurrentTable} DROP CONSTRAINT test_view_fault"
                : $"DROP TRIGGER test_fault ON {schema}.events; DROP FUNCTION {schema}.test_fault()"
        );
        await using var fresh = Scope(provider, Alpha);
        var freshDatabase = Context(fresh);
        await using var freshTransaction = await freshDatabase.Database.BeginTransactionAsync(
            Token
        );
        Proposed(await AppendAsync(fresh, 3, WinningBatch));
        await freshDatabase.SaveChangesAsync(Token);
        await freshTransaction.CommitAsync(Token);
        await AssertReadAsync(provider, (5, 20m));
    }

    [Fact]
    public async Task ASecondFailingSaveRollsBackTheFirstStreamToo()
    {
        string connection = await SeedAsync();
        string schema = Schema;
        await using var provider = Provider(connection);
        Guid secondId = Guid.NewGuid();
        await ExecuteAsync(
            connection,
            $"ALTER TABLE {schema}.events ADD CONSTRAINT test_event_fault CHECK (stream_version <> 1) NOT VALID"
        );
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, 3, WinningBatch));
            await database.SaveChangesAsync(Token);
            Proposed(await CreateAsync(write, secondId));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                database.SaveChangesAsync(Token)
            );
            Assert.False(IsConflict(failure));
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await AssertReadAsync(provider, (3, 5m));
        await AssertReadAsync(provider, null, id: secondId);
        Assert.Equal(3, await EventCountAsync(connection));
    }

    [Fact]
    public async Task InlineCommandsDoNotCertifyHistoryThatOnlyLiveReadsInspect()
    {
        string connection = await SeedAsync();
        await ExecuteAsync(connection, $"DELETE FROM {Schema}.events WHERE stream_version = 2");
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, 3, WinningBatch));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using var read = Scope(provider, Alpha);
        Assert.Equal((5, 20m), await CurrentAsync(read));
        await Assert.ThrowsAsync<EventHistoryException>(async () =>
        {
            await read
                .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                .ReadCurrentAsync(Id, Token);
        });
        Assert.Equal(4, await EventCountAsync(connection));
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
        return connection;
    }

    private async Task<string> SeedAsync()
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        await using (var create = Scope(provider, Alpha))
        {
            var database = Context(create);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await CreateAsync(create));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using (var append = Scope(provider, Alpha))
        {
            var database = Context(append);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(append, 1, InitialBatch));
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

    private static bool IsConflict(DbUpdateException failure) =>
        InventoryAppendFailures.IsVersionConflict(failure);

    private static async Task<StockPositionChangeResult> CreateAsync(
        AsyncServiceScope scope,
        Guid? id = null,
        long expected = 0
    )
    {
        Guid streamId = id ?? Id;
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
    }

    private static async Task<StockPositionChangeResult> AppendAsync(
        AsyncServiceScope scope,
        long expected,
        decimal[] quantities,
        DateTimeOffset? time = null
    )
    {
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
    }

    private static (long Version, decimal Amount) Proposed(StockPositionChangeResult result) =>
        result switch
        {
            StockPositionChangeResult.Changed staged => (
                staged.Proposed.Version,
                staged.Proposed.OnHand
            ),
            _ => throw new InvalidOperationException("Expected a staged business proposal."),
        };

    private static string Status(StockPositionChangeResult result) =>
        result switch
        {
            StockPositionChangeResult.NotFound => "not-found",
            StockPositionChangeResult.Conflict => "conflict",
            _ => throw new InvalidOperationException("Expected a preflight rejection."),
        };

    private static async Task AssertReadAsync(
        ServiceProvider provider,
        (long Version, decimal Amount)? expected,
        string owner = Alpha,
        Guid? id = null
    )
    {
        await using var read = Scope(provider, owner);
        Guid streamId = id ?? Id;
        var state = await read
            .ServiceProvider.GetRequiredService<IStockPositionHistory>()
            .ReadCurrentAsync(streamId, Token);
        (long Version, decimal Amount)? actual = state is null
            ? null
            : (state.Version, state.OnHand);
        Assert.Equal(expected, actual);
        Assert.Equal(expected, await CurrentAsync(read, streamId));
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<long> EventCountAsync(string connection)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {Schema}.events WHERE organization_key = @owner",
            database
        );
        command.Parameters.AddWithValue("owner", Alpha);
        return (long)(await command.ExecuteScalarAsync(Token))!;
    }

    private static Task FaultAsync(string connection, string fault)
    {
        string schema = Schema;
        string statement = fault switch
        {
            "current" =>
                $"ALTER TABLE {schema}.{CurrentTable} ADD CONSTRAINT test_view_fault CHECK (version <= 3)",
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
