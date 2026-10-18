using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo.Tests;

public sealed class StockRegenerationTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private const string Owner = "regeneration-owner";
    private static readonly Guid Id = Guid.Parse("e5330000-0000-0000-0000-000000000001");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaintenanceOnlyCompositionRepairsAndCanBeRepeatedWithoutChangingFacts(
        bool missing
    )
    {
        string connection = await SeedAsync();
        string facts = await FactsAsync(connection);
        await SqlAsync(
            connection,
            missing
                ? "DELETE FROM inventory.stock_position_current"
                : "UPDATE inventory.stock_position_current SET state = '[]'::jsonb"
        );
        await using var provider = Maintenance(connection);
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Null(scope.ServiceProvider.GetService<IStockPositionCommands>());
            Assert.Null(scope.ServiceProvider.GetService<IStockPositionQueries>());
        }

        using var output = new StringWriter();
        Assert.Equal(0, await RunAsync(provider, output));
        Assert.Equal(["started", "replayed", "completed"], Phases(output));
        Assert.Equal(3, Reports(output)[^1].GetProperty("version").GetInt64());
        string stamp = await ScalarAsync(
            connection,
            "SELECT concurrency_stamp::text FROM inventory.event_streams"
        );
        Assert.Equal(0, await RunAsync(provider, new StringWriter()));
        Assert.NotEqual(
            stamp,
            await ScalarAsync(
                connection,
                "SELECT concurrency_stamp::text FROM inventory.event_streams"
            )
        );
        Assert.Equal(facts, await FactsAsync(connection));
        Assert.Equal((3L, 5m), await ReadAsync(connection));

        await ReceiveAsync(connection);
        Assert.Equal((4L, 6m), await ReadAsync(connection));
    }

    [Theory]
    [InlineData("regeneration-owner", true)]
    [InlineData("another-owner", false)]
    public async Task MissingOrForeignTargetReportsNotFoundWithoutChangingData(
        string owner,
        bool missing
    )
    {
        string connection = await SeedAsync();
        string facts = await FactsAsync(connection);
        string stamp = await ScalarAsync(
            connection,
            "SELECT concurrency_stamp::text FROM inventory.event_streams"
        );
        await using var provider = Maintenance(connection);
        using var output = new StringWriter();
        Assert.Equal(
            2,
            await StockRegeneration.RunAsync(
                provider.GetRequiredService<IServiceScopeFactory>(),
                owner,
                missing ? Guid.NewGuid() : Id,
                output,
                Token
            )
        );
        Assert.Equal(["started", "not-found"], Phases(output));
        Assert.Equal(facts, await FactsAsync(connection));
        Assert.Equal(
            stamp,
            await ScalarAsync(
                connection,
                "SELECT concurrency_stamp::text FROM inventory.event_streams"
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonretryableHistoryAndSqlFailuresReportOnceThenAllowExplicitRestart(
        bool sqlFault
    )
    {
        string connection = await SeedAsync();
        await SqlAsync(
            connection,
            sqlFault
                ? "UPDATE inventory.stock_position_current SET state = '[]'::jsonb; ALTER TABLE inventory.stock_position_current ADD CONSTRAINT regeneration_fault CHECK (state = '[]'::jsonb) NOT VALID"
                : "UPDATE inventory.events SET schema_version = 999 WHERE stream_version = 1"
        );
        string facts = await FactsAsync(connection);
        string stamp = await ScalarAsync(
            connection,
            "SELECT concurrency_stamp::text FROM inventory.event_streams"
        );
        await using var provider = Maintenance(connection);
        using var output = new StringWriter();
        Assert.Equal(1, await RunAsync(provider, output));
        Assert.Equal(
            sqlFault ? ["started", "replayed", "failed"] : ["started", "failed"],
            Phases(output)
        );
        Assert.DoesNotContain("writer-conflict", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(facts, await FactsAsync(connection));
        Assert.Equal(
            stamp,
            await ScalarAsync(
                connection,
                "SELECT concurrency_stamp::text FROM inventory.event_streams"
            )
        );
        await SqlAsync(
            connection,
            sqlFault
                ? "ALTER TABLE inventory.stock_position_current DROP CONSTRAINT regeneration_fault"
                : "UPDATE inventory.events SET schema_version = 1 WHERE stream_version = 1"
        );
        Assert.Equal(0, await RunAsync(provider, new StringWriter()));
        Assert.Equal((3L, 5m), await ReadAsync(connection));
    }

    [Fact]
    public async Task CancellationAfterSqlRollsBackAndFreshInvocationRestarts()
    {
        string connection = await SeedAsync();
        await SqlAsync(
            connection,
            "UPDATE inventory.stock_position_current SET state = '[]'::jsonb"
        );
        string stamp = await ScalarAsync(
            connection,
            "SELECT concurrency_stamp::text FROM inventory.event_streams"
        );
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var barrier = new SaveBarrier { AfterSave = cancellation.CancelAsync };
        await using var provider = Maintenance(connection, barrier);
        using var output = new StringWriter();
        Assert.Equal(130, await RunAsync(provider, output, cancellation.Token));
        Assert.Equal(["started", "replayed", "cancelled"], Phases(output));
        Assert.Equal(
            "[]",
            await ScalarAsync(
                connection,
                "SELECT state::text FROM inventory.stock_position_current"
            )
        );
        Assert.Equal(
            stamp,
            await ScalarAsync(
                connection,
                "SELECT concurrency_stamp::text FROM inventory.event_streams"
            )
        );
        await using var restarted = Maintenance(connection);
        Assert.Equal(0, await RunAsync(restarted, new StringWriter()));
        Assert.Equal((3L, 5m), await ReadAsync(connection));
        AssertDisposed(barrier);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWritersCauseFreshReplayOrBoundedExhaustion(bool exhaust)
    {
        string connection = await SeedAsync();
        int writes = 0;
        var barrier = new SaveBarrier
        {
            BeforeSave = async () =>
            {
                if (exhaust || writes == 0)
                {
                    await ReceiveAsync(connection);
                    writes++;
                }
            },
        };
        await using var provider = Maintenance(connection, barrier);
        using var output = new StringWriter();
        Assert.Equal(exhaust ? 1 : 0, await RunAsync(provider, output));
        Assert.Equal(
            exhaust
                ?
                [
                    "started",
                    "replayed",
                    "conflict",
                    "started",
                    "replayed",
                    "conflict",
                    "started",
                    "replayed",
                    "failed",
                ]
                : ["started", "replayed", "conflict", "started", "replayed", "completed"],
            Phases(output)
        );
        Assert.Equal(exhaust ? 3 : 2, barrier.Contexts.Distinct().Count());
        Assert.Equal((3L + writes, 5m + writes), await ReadAsync(connection));
        AssertDisposed(barrier);
        if (exhaust)
        {
            await using var restarted = Maintenance(connection);
            Assert.Equal(0, await RunAsync(restarted, new StringWriter()));
        }
    }

    [Fact]
    public async Task CompletedRepairInvalidatesAWriterThatAlreadyDecided()
    {
        string connection = await SeedAsync();
        await using var writer = Consumer(connection);
        await using var scope = Scope(writer);
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<StockPositionChangeResult.Changed>(
            await scope
                .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                .ReceiveAsync(new ReceiveStock(Id, 3, [new StockReceipt(1)]), Token)
        );
        await using var repair = Maintenance(connection);
        Assert.Equal(0, await RunAsync(repair, new StringWriter()));
        var failure = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            database.SaveChangesAsync(Token)
        );
        Assert.True(InventoryAppendFailures.IsVersionConflict(failure));
        await transaction.RollbackAsync(Token);
        Assert.Equal((3L, 5m), await ReadAsync(connection));
        await ReceiveAsync(connection);
        Assert.Equal((4L, 6m), await ReadAsync(connection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedExecutableRollsBackAndTheSameCommandRestartsInANewProcess(
        bool graceful
    )
    {
        string connection = await SeedAsync();
        await SqlAsync(
            connection,
            "UPDATE inventory.stock_position_current SET state = '[]'::jsonb"
        );
        string stamp = await ScalarAsync(
            connection,
            "SELECT concurrency_stamp::text FROM inventory.event_streams"
        );
        await using var blocker = new NpgsqlConnection(connection);
        await blocker.OpenAsync(Token);
        await using var transaction = await blocker.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "SELECT 1 FROM inventory.event_streams FOR UPDATE",
                blocker,
                transaction
            )
        )
            await command.ExecuteScalarAsync(Token);

        using (var child = StartProcess(connection))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                Assert.Equal(
                    "started",
                    Phase((await child.StandardOutput.ReadLineAsync(timeout.Token))!)
                );
                Assert.Equal(
                    "replayed",
                    Phase((await child.StandardOutput.ReadLineAsync(timeout.Token))!)
                );
                await WaitForBlockedProcessAsync(connection);
                if (graceful)
                {
                    // The executable's real Ctrl+C/SIGINT path on the Linux CI host.
                    var signal = new ProcessStartInfo("kill") { UseShellExecute = false };
                    signal.ArgumentList.Add("-INT");
                    signal.ArgumentList.Add(
                        child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    );
                    using var sender = Process.Start(signal)!;
                    await sender.WaitForExitAsync(timeout.Token);
                    Assert.Equal(0, sender.ExitCode);
                    Assert.Equal(
                        "cancelled",
                        Phase((await child.StandardOutput.ReadLineAsync(timeout.Token))!)
                    );
                }
                else
                    child.Kill(entireProcessTree: true);

                await child.WaitForExitAsync(timeout.Token);
                if (graceful)
                    Assert.Equal(130, child.ExitCode);
                else
                    Assert.NotEqual(0, child.ExitCode);
            }
            finally
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
            }
        }

        await transaction.RollbackAsync(Token);
        Assert.Equal(
            stamp,
            await ScalarAsync(
                connection,
                "SELECT concurrency_stamp::text FROM inventory.event_streams"
            )
        );
        Assert.Equal(
            "[]",
            await ScalarAsync(
                connection,
                "SELECT state::text FROM inventory.stock_position_current"
            )
        );
        for (int repeat = 0; repeat < 2; repeat++)
        {
            using var restarted = StartProcess(connection);
            string output = await restarted.StandardOutput.ReadToEndAsync(Token);
            string error = await restarted.StandardError.ReadToEndAsync(Token);
            await restarted.WaitForExitAsync(Token);
            Assert.True(restarted.ExitCode == 0, error + output);
            Assert.Contains("\"phase\":\"completed\"", output, StringComparison.Ordinal);
        }

        Assert.Equal((3L, 5m), await ReadAsync(connection));
    }

    [Fact]
    public async Task LostCommitAcknowledgmentIsIndeterminateAndExplicitReplayConverges()
    {
        string connection = await SeedAsync();
        await SqlAsync(
            connection,
            "UPDATE inventory.stock_position_current SET state = '[]'::jsonb"
        );
        string facts = await FactsAsync(connection);
        var services = StockRegeneration.CreateServices(connection);
        services.AddDbContext<InventoryDbContext>(options =>
            options.AddInterceptors(new LostAcknowledgment())
        );
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        Assert.Equal(4, await RunAsync(provider, output));
        Assert.Equal(["started", "replayed", "indeterminate"], Phases(output));
        // The native transaction really committed; the interceptor faults its acknowledgment.
        Assert.Equal((3L, 5m), await ReadAsync(connection));
        Assert.Equal(facts, await FactsAsync(connection));
        await using var restarted = Maintenance(connection);
        Assert.Equal(0, await RunAsync(restarted, new StringWriter()));
        Assert.Equal(facts, await FactsAsync(connection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutorRetriesVerifiedWriterReadRaceButRejectsPersistentAheadState(
        bool writerAdvanced
    )
    {
        string connection = await SeedAsync();
        var observation = new QueryObservation("inventory");
        if (writerAdvanced)
            observation.BeforeInlineRead = _ => ReceiveAsync(connection);
        else
            await SqlAsync(connection, "UPDATE inventory.stock_position_current SET version = 4");

        await using var provider = Maintenance(connection, observation);
        using var output = new StringWriter();
        Assert.Equal(writerAdvanced ? 0 : 1, await RunAsync(provider, output));
        Assert.Equal(
            writerAdvanced
                ? ["started", "conflict", "started", "replayed", "completed"]
                : ["started", "failed"],
            Phases(output)
        );
        if (!writerAdvanced)
            Assert.Equal(
                "InvalidDataException",
                Reports(output)[^1].GetProperty("reason").GetString()
            );
        string facts = await FactsAsync(connection);
        string stamp = await ScalarAsync(
            connection,
            "SELECT concurrency_stamp::text FROM inventory.event_streams"
        );
        await using var restarted = Maintenance(connection);
        Assert.Equal(writerAdvanced ? 0 : 1, await RunAsync(restarted, new StringWriter()));
        Assert.Equal(facts, await FactsAsync(connection));
        if (writerAdvanced)
            Assert.Equal((4L, 6m), await ReadAsync(connection));
        else
            Assert.Equal(
                stamp,
                await ScalarAsync(
                    connection,
                    "SELECT concurrency_stamp::text FROM inventory.event_streams"
                )
            );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumerScopeFactoryDisposesAfterInitializationFailureAndRetainsCleanupErrors(
        bool cleanupFails
    )
    {
        string connection = await SeedAsync();
        string facts = await FactsAsync(connection);
        string stamp = await ScalarAsync(
            connection,
            "SELECT concurrency_stamp::text FROM inventory.event_streams"
        );
        var created = new List<FailedTenantInitializer>();
        var services = StockRegeneration.CreateServices(connection);
        services.AddScoped<ITenantContextInitializer>(_ =>
        {
            var initializer = new FailedTenantInitializer(cleanupFails);
            created.Add(initializer);
            return initializer;
        });
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        Assert.Equal(1, await RunAsync(provider, output));
        Assert.Equal(["failed"], Phases(output));
        Assert.Equal(1, Reports(output)[0].GetProperty("attempt").GetInt32());
        Assert.Equal(
            cleanupFails ? "AggregateException" : "InvalidOperationException",
            Reports(output)[0].GetProperty("reason").GetString()
        );
        var failed = Assert.Single(created);
        Assert.True(failed.Disposed);
        Assert.Equal(Owner, failed.Context!.RequireTenant().Value);
        var result = await StockPositionMaintenance.RegenerateAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Owner,
            Id,
            new() { MaxAttempts = 3 },
            progress: null,
            Token
        );
        Assert.Equal(AggregateRegenerationStatus.Failed, result.Status);
        Assert.Equal(1, result.Attempts);
        if (cleanupFails)
        {
            var errors = Assert.IsType<AggregateException>(result.Error).InnerExceptions;
            Assert.Collection(
                errors,
                error => Assert.IsType<InvalidOperationException>(error),
                error => Assert.IsType<IOException>(error)
            );
        }
        else
            Assert.IsType<InvalidOperationException>(result.Error);

        Assert.Equal(2, created.Count);
        Assert.All(created, item => Assert.True(item.Disposed));
        Assert.Equal(facts, await FactsAsync(connection));
        Assert.Equal(
            stamp,
            await ScalarAsync(
                connection,
                "SELECT concurrency_stamp::text FROM inventory.event_streams"
            )
        );
    }

    private static ServiceProvider Maintenance(string connection, IInterceptor? barrier = null)
    {
        var services = StockRegeneration.CreateServices(connection);
        if (barrier is not null)
            services.AddDbContext<InventoryDbContext>(options => options.AddInterceptors(barrier));

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private static ServiceProvider Consumer(string connection)
    {
        var services = StockRegeneration.CreateServices(connection);
        services.AddStockPositionCommands();
        services.AddStockPositionQueries();
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private async Task<string> SeedAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Consumer(connection);
        await using (var scope = Scope(provider))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await database.Database.MigrateAsync(Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.Changed>(
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .OpenAsync(
                        new OpenStockPosition(Id, Guid.NewGuid(), Guid.NewGuid(), "EA"),
                        Token
                    )
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await ReceiveAsync(connection, 1, [new StockReceipt(2), new StockReceipt(3)]);
        return connection;
    }

    private static async Task ReceiveAsync(
        string connection,
        long? expected = null,
        StockReceipt[]? receipts = null
    )
    {
        await using var provider = Consumer(connection);
        await using var scope = Scope(provider);
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        long version =
            expected
            ?? (
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                    .ReadCurrentAsync(Id, Token)
            )!.Version;
        Assert.IsType<StockPositionChangeResult.Changed>(
            await scope
                .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                .ReceiveAsync(
                    new ReceiveStock(Id, version, receipts ?? [new StockReceipt(1)]),
                    Token
                )
        );
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static async Task<(long, decimal)> ReadAsync(string connection)
    {
        await using var provider = Consumer(connection);
        await using var scope = Scope(provider);
        var current = (
            await scope
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(Id, Token)
        )!;
        return (current.Version, current.OnHand);
    }

    private static AsyncServiceScope Scope(ServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(Owner)));
        return scope;
    }

    private static Task<int> RunAsync(
        ServiceProvider provider,
        TextWriter output,
        CancellationToken? token = null
    ) =>
        StockRegeneration.RunAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Owner,
            Id,
            output,
            token ?? Token
        );

    private static JsonElement[] Reports(StringWriter output) =>
        output
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
            .ToArray();

    private static string[] Phases(StringWriter output) =>
        Reports(output).Select(report => report.GetProperty("phase").GetString()!).ToArray();

    private static string? Phase(string line) =>
        JsonSerializer.Deserialize<JsonElement>(line).GetProperty("phase").GetString();

    private static Task<string> FactsAsync(string connection) =>
        ScalarAsync(
            connection,
            """
            SELECT jsonb_build_object(
                'headers', (SELECT jsonb_agg(to_jsonb(s) - 'concurrency_stamp') FROM inventory.event_streams s),
                'events', (SELECT jsonb_agg(to_jsonb(e) ORDER BY stream_version) FROM inventory.events e))::text
            """
        );

    private static async Task<string> ScalarAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        return (string)(await command.ExecuteScalarAsync(Token))!;
    }

    private static async Task SqlAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static Process StartProcess(string connection)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(StockRegeneration).Assembly.Location);
        start.ArgumentList.Add("--regenerate-stock");
        start.ArgumentList.Add(Owner);
        start.ArgumentList.Add(Id.ToString());
        start.Environment["WHOLESALE_DEMO_CONNECTION_STRING"] = new NpgsqlConnectionStringBuilder(
            connection
        )
        {
            ApplicationName = "es3-child",
        }.ConnectionString;
        return Process.Start(start)!;
    }

    private static async Task WaitForBlockedProcessAsync(string connection)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(timeout.Token);
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'es3-child' AND wait_event_type = 'Lock')",
                database
            );
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!)
                return;

            await Task.Delay(25, timeout.Token);
        }
    }

    private static void AssertDisposed(SaveBarrier barrier)
    {
        foreach (var context in barrier.Contexts)
            Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker.Entries().ToArray());
    }

    private sealed class FailedTenantInitializer(bool cleanupFails)
        : ITenantContextInitializer,
            IDisposable
    {
        internal TenantContext? Context { get; private set; }
        internal bool Disposed { get; private set; }

        public void Initialize(TenantContext context)
        {
            Context = context;
            throw new InvalidOperationException("Injected tenant establishment failure.");
        }

        public void Dispose()
        {
            Disposed = true;
            if (cleanupFails)
                throw new IOException("Injected initializer cleanup failure.");
        }
    }

    private sealed class LostAcknowledgment : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) =>
            throw new IOException("Injected acknowledgment loss after the real PostgreSQL commit.");
    }

    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        internal Func<Task>? BeforeSave { get; init; }
        internal Func<Task>? AfterSave { get; init; }
        internal List<DbContext> Contexts { get; } = [];

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Contexts.Add(eventData.Context!);
            if (BeforeSave is not null)
                await BeforeSave();

            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (AfterSave is not null)
                await AfterSave();

            return result;
        }
    }
}
