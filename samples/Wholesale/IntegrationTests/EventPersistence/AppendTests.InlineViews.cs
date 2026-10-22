using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.EventPersistence;

public sealed partial class AppendTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InlineEditWorksWithHistoryReadsDeniedWhileLiveReplayFails(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        string schema = Schema(inventory);
        string role = "inline_writer_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(
            connection,
            $"""
            CREATE ROLE {role} LOGIN PASSWORD 'test-only-inline';
            GRANT USAGE ON SCHEMA {schema} TO {role};
            GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA {schema} TO {role};
            REVOKE SELECT ON {schema}.events FROM {role};
            """
        );
        var restricted = new NpgsqlConnectionStringBuilder(connection)
        {
            Username = role,
            Password = "test-only-inline",
        };
        await using var provider = Provider(restricted.ConnectionString);
        await using (var read = Scope(provider, Alpha))
        {
            Assert.Equal((3, inventory ? 5m : 37.5m), await CurrentAsync(read, inventory));
            var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
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
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        }
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                (5, inventory ? 20m : 100m),
                Proposed(await AppendAsync(write, inventory, 3, WinningBatch))
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using var administrator = Provider(connection);
        await AssertReadAsync(administrator, inventory, (5, inventory ? 20m : 100m));
    }

    [Theory]
    [InlineData(true, "missing")]
    [InlineData(true, "behind")]
    [InlineData(true, "timestamp")]
    [InlineData(false, "missing")]
    [InlineData(false, "behind")]
    [InlineData(false, "timestamp")]
    public async Task AggregateStateDamageRejectsStagingAndQueriesWithoutRepair(
        bool inventory,
        string damage
    )
    {
        string connection = await SeedAsync(inventory);
        string table = CurrentTable(inventory);
        string target = $"{Schema(inventory)}.{table}";
        await ExecuteAsync(
            connection,
            damage switch
            {
                "missing" => $"DELETE FROM {target}",
                "behind" => $"UPDATE {target} SET version = 2",
                "timestamp" =>
                    $"UPDATE {target} SET recorded_at = recorded_at - interval '1 second'",
                _ => throw new ArgumentOutOfRangeException(nameof(damage)),
            }
        );
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                AppendAsync(write, inventory, 3, WinningBatch)
            );
            Assert.Empty(database.ChangeTracker.Entries());
        }
        await using var read = Scope(provider, Alpha);
        await Assert.ThrowsAsync<InvalidDataException>(() => CurrentAsync(read, inventory));
        if (!inventory)
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                read
                    .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
                    .ReadSummaryAsync(Id, Token)
            );
        Assert.Equal(3, await EventCountAsync(connection, inventory));
        if (damage == "missing")
        {
            await using var sql = new NpgsqlConnection(connection);
            await sql.OpenAsync(Token);
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {target}", sql);
            Assert.Equal(0L, await count.ExecuteScalarAsync(Token));
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task CommitBetweenHeaderAndViewReadsIsConcurrencyRatherThanCorruption(
        bool inventory,
        bool query
    )
    {
        string connection = await SeedAsync(inventory);
        await using var winner = Provider(connection);
        var observation = new QueryObservation(Schema(inventory))
        {
            BeforeInlineRead = async cancellation =>
            {
                await using var scope = Scope(winner, Alpha);
                var database = Context(scope, inventory);
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellation
                );
                Proposed(await AppendAsync(scope, inventory, 3, WinningBatch));
                await database.SaveChangesAsync(cancellation);
                await transaction.CommitAsync(cancellation);
            },
        };
        await using var provider = Provider(connection, observation);
        await using (var scope = Scope(provider, Alpha))
        {
            if (query)
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                    CurrentAsync(scope, inventory)
                );
            else
            {
                var database = Context(scope, inventory);
                await using var transaction = await database.Database.BeginTransactionAsync(Token);
                Assert.Equal(
                    "conflict",
                    Status(await AppendAsync(scope, inventory, 3, InitialBatch))
                );
                Assert.Empty(database.ChangeTracker.Entries());
            }
        }
        await AssertReadAsync(winner, inventory, (5, inventory ? 20m : 100m));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidLaterInputCannotLeaveAnAcceptedPartialBatch(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        await using var provider = Provider(connection);
        await using (var scope = Scope(provider, Alpha))
        {
            var database = Context(scope, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                AppendAsync(scope, inventory, 3, [7m, -1m])
            );
            Assert.Empty(database.ChangeTracker.Entries());
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        await CommitBatchAsync(provider, inventory, 3, WinningBatch);
        await AssertReadAsync(provider, inventory, (5, inventory ? 20m : 100m));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task UnrepresentableCandidateRejectsTheWholeBatchBeforeTracking(
        bool inventory,
        bool total
    )
    {
        string connection = await SeedAsync(inventory);
        await using var provider = Provider(connection);
        await using (var scope = Scope(provider, Alpha))
        {
            var database = Context(scope, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            if (total)
                await Assert.ThrowsAsync<OverflowException>(() =>
                    scope
                        .ServiceProvider.WithClock(Changed)
                        .GetRequiredService<IPurchaseOrderCommands>()
                        .ChangeLinesAsync(
                            new ChangePurchaseOrderLines(
                                Id,
                                3,
                                [
                                    new PurchaseOrderLine("MAX-A", decimal.MaxValue, 1),
                                    new PurchaseOrderLine("MAX-B", 1, 1),
                                ]
                            ),
                            Token
                        )
                );
            else
                await Assert.ThrowsAsync<OverflowException>(() =>
                    AppendAsync(scope, inventory, 3, [7m, decimal.MaxValue])
                );
            Assert.Empty(database.ChangeTracker.Entries());
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        await CommitBatchAsync(provider, inventory, 3, WinningBatch);
        await AssertReadAsync(provider, inventory, (5, inventory ? 20m : 100m));
    }

    [Fact]
    public async Task FinalCandidateValidationAllowsATemporaryBatchTotalToBeReplaced()
    {
        string connection = await SeedAsync(false);
        await using var provider = Provider(connection);
        await using (var scope = Scope(provider, Alpha))
        {
            var database = Context(scope, false);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var result = await scope
                .ServiceProvider.WithClock(Changed)
                .GetRequiredService<IPurchaseOrderCommands>()
                .ChangeLinesAsync(
                    new ChangePurchaseOrderLines(
                        Id,
                        3,
                        [
                            new PurchaseOrderLine("ITEM-1", decimal.MaxValue, 1),
                            new PurchaseOrderLine("ITEM-2", 1, 1),
                            new PurchaseOrderLine("ITEM-1", 2, 1),
                        ]
                    ),
                    Token
                );
            Assert.Equal((6, 3m), Proposed(result));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, false, (6, 3m));
        await using var read = Scope(provider, Alpha);
        var summary = (
            await read
                .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
                .ReadSummaryAsync(Id, Token)
        )!;
        Assert.Equal(2, summary.LineCount);
    }

    [Fact]
    public async Task DerivedSummaryReflectsOnlyTheTargetLineReplacement()
    {
        string connection = await SeedAsync(false);
        await using var provider = Provider(connection);
        await using (var scope = Scope(provider, Alpha))
        {
            var database = Context(scope, false);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                (5, 47m),
                Proposed(
                    await scope
                        .ServiceProvider.WithClock(Changed)
                        .GetRequiredService<IPurchaseOrderCommands>()
                        .ChangeLinesAsync(
                            new ChangePurchaseOrderLines(
                                Id,
                                3,
                                [
                                    new PurchaseOrderLine("ITEM-2", 4, 5),
                                    new PurchaseOrderLine("ITEM-1", 3, 9),
                                ]
                            ),
                            Token
                        )
                )
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, false, (5, 47m));
        await using var read = Scope(provider, Alpha);
        var queries = read.ServiceProvider.GetRequiredService<IPurchaseOrderQueries>();
        var summary = (await queries.ReadSummaryAsync(Id, Token))!;
        Assert.Equal(
            ("APPEND-1", "EUR", 2, 47m),
            (summary.Code, summary.Currency, summary.LineCount, summary.Total)
        );
        var state = (await queries.ReadCurrentAsync(Id, Token))!;
        Assert.Collection(
            state.Lines.OrderBy(line => line.ItemCode),
            line => Assert.Equal(new PurchaseOrderLine("ITEM-1", 3, 9), line),
            line => Assert.Equal(new PurchaseOrderLine("ITEM-2", 4, 5), line)
        );
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task CommittedViewQueriesRequireAnEstablishedSelectedTenant(
        bool inventory,
        bool tenantless
    )
    {
        string connection = await SeedAsync(inventory);
        await using var provider = Provider(connection);
        await using var scope = provider.CreateAsyncScope();
        if (tenantless)
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            CurrentAsync(scope, inventory)
        );
        if (!inventory)
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
                    .ReadSummaryAsync(Id, Token)
            );
    }

    private static string CurrentTable(bool inventory) =>
        inventory ? "stock_position_current" : "purchase_order_current";

    private static async Task<(long Version, decimal Amount)?> CurrentAsync(
        AsyncServiceScope scope,
        bool inventory,
        Guid? id = null
    )
    {
        if (inventory)
        {
            var current = await scope
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(id ?? Id, Token);
            return current is null ? null : (current.Version, current.OnHand);
        }
        var order = await scope
            .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
            .ReadCurrentAsync(id ?? Id, Token);
        return order is null ? null : (order.Version, order.Total);
    }

    private static async Task CommitBatchAsync(
        ServiceProvider provider,
        bool inventory,
        long expected,
        decimal[] batch
    )
    {
        await using var scope = Scope(provider, Alpha);
        var database = Context(scope, inventory);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Proposed(await AppendAsync(scope, inventory, expected, batch));
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }
}
