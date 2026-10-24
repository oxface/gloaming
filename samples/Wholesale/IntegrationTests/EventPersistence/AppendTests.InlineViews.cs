using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.EventPersistence;

public sealed partial class AppendTests
{
    [Fact]
    public async Task InlineEditWorksWithHistoryReadsDeniedWhileLiveReplayFails()
    {
        string connection = await SeedAsync();
        string schema = Schema;
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
            Assert.Equal((3, 5m), await CurrentAsync(read));
            var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await read
                    .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                    .ReadCurrentAsync(Id, Token);
            });
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        }

        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((5, 20m), Proposed(await AppendAsync(write, 3, WinningBatch)));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using var administrator = Provider(connection);
        await AssertReadAsync(administrator, (5, 20m));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("behind")]
    [InlineData("timestamp")]
    public async Task AggregateStateDamageRejectsStagingAndQueriesWithoutRepair(string damage)
    {
        string connection = await SeedAsync();
        string table = CurrentTable;
        string target = $"{Schema}.{table}";
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
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                AppendAsync(write, 3, WinningBatch)
            );
            Assert.Empty(database.ChangeTracker.Entries());
        }

        await using var read = Scope(provider, Alpha);
        await Assert.ThrowsAsync<InvalidDataException>(() => CurrentAsync(read));
        Assert.Equal(3, await EventCountAsync(connection));
        if (damage == "missing")
        {
            await using var sql = new NpgsqlConnection(connection);
            await sql.OpenAsync(Token);
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {target}", sql);
            Assert.Equal(0L, await count.ExecuteScalarAsync(Token));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitBetweenHeaderAndViewReadsIsConcurrencyRatherThanCorruption(bool query)
    {
        string connection = await SeedAsync();
        await using var winner = Provider(connection);
        var observation = new QueryObservation(Schema)
        {
            BeforeInlineRead = async cancellation =>
            {
                await using var scope = Scope(winner, Alpha);
                var database = Context(scope);
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellation
                );
                Proposed(await AppendAsync(scope, 3, WinningBatch));
                await database.SaveChangesAsync(cancellation);
                await transaction.CommitAsync(cancellation);
            },
        };
        await using var provider = Provider(connection, observation);
        await using (var scope = Scope(provider, Alpha))
        {
            if (query)
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => CurrentAsync(scope));
            else
            {
                var database = Context(scope);
                await using var transaction = await database.Database.BeginTransactionAsync(Token);
                Assert.Equal("conflict", Status(await AppendAsync(scope, 3, InitialBatch)));
                Assert.Empty(database.ChangeTracker.Entries());
            }
        }

        await AssertReadAsync(winner, (5, 20m));
    }

    [Fact]
    public async Task InvalidLaterInputCannotLeaveAnAcceptedPartialBatch()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var scope = Scope(provider, Alpha))
        {
            var database = Context(scope);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                AppendAsync(scope, 3, [7m, -1m])
            );
            Assert.Empty(database.ChangeTracker.Entries());
        }

        await AssertReadAsync(provider, (3, 5m));
        await CommitBatchAsync(provider, 3, WinningBatch);
        await AssertReadAsync(provider, (5, 20m));
    }

    [Fact]
    public async Task UnrepresentableCandidateRejectsTheWholeBatchBeforeTracking()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var scope = Scope(provider, Alpha))
        {
            var database = Context(scope);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<OverflowException>(() =>
                AppendAsync(scope, 3, [7m, decimal.MaxValue])
            );
            Assert.Empty(database.ChangeTracker.Entries());
        }

        await AssertReadAsync(provider, (3, 5m));
        await CommitBatchAsync(provider, 3, WinningBatch);
        await AssertReadAsync(provider, (5, 20m));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedViewQueriesRequireAnEstablishedSelectedTenant(bool tenantless)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var scope = provider.CreateAsyncScope();
        if (tenantless)
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => CurrentAsync(scope));
    }

    private const string CurrentTable = "stock_position_current";

    private static async Task<(long Version, decimal Amount)?> CurrentAsync(
        AsyncServiceScope scope,
        Guid? id = null
    )
    {
        var current = await scope
            .ServiceProvider.GetRequiredService<IStockPositionQueries>()
            .ReadCurrentAsync(id ?? Id, Token);
        return current is null ? null : (current.Version, current.OnHand);
    }

    private static async Task CommitBatchAsync(
        ServiceProvider provider,
        long expected,
        decimal[] batch
    )
    {
        await using var scope = Scope(provider, Alpha);
        var database = Context(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Proposed(await AppendAsync(scope, expected, batch));
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }
}
