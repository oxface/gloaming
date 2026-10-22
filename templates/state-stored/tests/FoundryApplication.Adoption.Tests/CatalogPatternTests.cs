using ConsumerRoot.Catalog;
using ConsumerRoot.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ConsumerRoot.Adoption.Tests;

public sealed partial class CatalogJourneyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async global::System.Threading.Tasks.Task Commands_queries_and_native_concurrency_preserve_the_accepted_root()
    {
        await WithDatabaseAsync(
            async (services, _) =>
            {
                Guid first = Guid.Parse("10000000-0000-0000-0000-000000000001");
                Guid second = Guid.Parse("10000000-0000-0000-0000-000000000002");
                await CreateItemAsync(services, "tenant-a", first, "Alpha one", 40);
                await CreateItemAsync(services, "tenant-a", second, "Alpha two", 40);
                await CreateItemAsync(services, "tenant-a", Guid.NewGuid(), "Alpha cheap", 5);
                await CreateItemAsync(services, "tenant-b", Guid.NewGuid(), "Alpha foreign", 90);
                await using (var read = Operation(services, "tenant-a"))
                {
                    var queries = read.ServiceProvider.GetRequiredService<ICatalogQueries>();
                    var page = await queries.SearchAsync(new("Alpha", 30, Limit: 1), Token);
                    Assert.Equal(2, page.TotalCount);
                    Assert.Equal(
                        new CatalogItem(first, "Alpha one", 40, 1),
                        Assert.Single(page.Items)
                    );
                    Assert.Equal(
                        second,
                        Assert
                            .Single(
                                (
                                    await queries.SearchAsync(
                                        new("Alpha", 30, Offset: 1, Limit: 1),
                                        Token
                                    )
                                ).Items
                            )
                            .Id
                    );
                    var beyond = await queries.SearchAsync(
                        new("Alpha", 30, Offset: 2, Limit: 1),
                        Token
                    );
                    Assert.Equal(2, beyond.TotalCount);
                    Assert.Empty(beyond.Items);
                    Assert.Empty(
                        read.ServiceProvider.GetRequiredService<CatalogDbContext>()
                            .ChangeTracker.Entries()
                    );
                }

                await using (var rejected = Operation(services, "tenant-a"))
                {
                    var database = rejected.ServiceProvider.GetRequiredService<CatalogDbContext>();
                    await using var transaction = await database.Database.BeginTransactionAsync(
                        Token
                    );
                    var commands = rejected.ServiceProvider.GetRequiredService<ICatalogCommands>();
                    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                        commands.ChangeAsync(first, 1, "Rejected", 10_001, Token)
                    );
                    await Assert.ThrowsAsync<ArgumentException>(() =>
                        commands.ChangeAsync(first, 1, "Rejected", 1.123m, Token)
                    );
                    Assert.False(database.ChangeTracker.HasChanges());
                    await transaction.RollbackAsync(Token);
                }
                await using (var rollback = Operation(services, "tenant-a"))
                {
                    var database = rollback.ServiceProvider.GetRequiredService<CatalogDbContext>();
                    await using var transaction = await database.Database.BeginTransactionAsync(
                        Token
                    );
                    Assert.IsType<CatalogChange.Changed>(
                        await rollback
                            .ServiceProvider.GetRequiredService<ICatalogCommands>()
                            .ChangeAsync(first, 1, "Rolled back", 70, Token)
                    );
                    await database.SaveChangesAsync(Token);
                    Assert.Equal(
                        new CatalogItem(first, "Alpha one", 40, 1),
                        (await ReadAsync(services, "tenant-a")).Single(row => row.Id == first)
                    );
                    await transaction.RollbackAsync(Token);
                }

                await using var winner = Operation(services, "tenant-a");
                await using var loser = Operation(services, "tenant-a");
                var winningDb = winner.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var losingDb = loser.ServiceProvider.GetRequiredService<CatalogDbContext>();
                await using var winningTransaction = await winningDb.Database.BeginTransactionAsync(
                    Token
                );
                await using var losingTransaction = await losingDb.Database.BeginTransactionAsync(
                    Token
                );
                Assert.IsType<CatalogChange.Changed>(
                    await winner
                        .ServiceProvider.GetRequiredService<ICatalogCommands>()
                        .ChangeAsync(first, 1, "Winner", 60, Token)
                );
                Assert.IsType<CatalogChange.Changed>(
                    await loser
                        .ServiceProvider.GetRequiredService<ICatalogCommands>()
                        .ChangeAsync(first, 1, "Loser", 80, Token)
                );
                await winningDb.SaveChangesAsync(Token);
                await winningTransaction.CommitAsync(Token);
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                    losingDb.SaveChangesAsync(Token)
                );
                await losingTransaction.RollbackAsync(Token);
                Assert.Equal(
                    new CatalogItem(first, "Winner", 60, 2),
                    (await ReadAsync(services, "tenant-a")).Single(row => row.Id == first)
                );

                await using var fresh = Operation(services, "tenant-a");
                var freshDb = fresh.ServiceProvider.GetRequiredService<CatalogDbContext>();
                await using var freshTransaction = await freshDb.Database.BeginTransactionAsync(
                    Token
                );
                Assert.IsType<CatalogChange.Conflict>(
                    await fresh
                        .ServiceProvider.GetRequiredService<ICatalogCommands>()
                        .ChangeAsync(first, 1, "Stale", 90, Token)
                );
                Assert.False(freshDb.ChangeTracker.HasChanges());
                await freshTransaction.RollbackAsync(Token);
            }
        );
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Command_requires_a_caller_owned_native_transaction()
    {
        using var services = ConsumerRoot
            .Host.Composition.CreateServices("Host=127.0.0.1;Port=1;Database=unused;Timeout=1")
            .BuildServiceProvider();
        await using var scope = Operation(services, "tenant-a");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope
                .ServiceProvider.GetRequiredService<ICatalogCommands>()
                .CreateAsync(Guid.NewGuid(), "Item", 10, Token)
        );
    }

    private static AsyncServiceScope Operation(ServiceProvider services, string tenant)
    {
        var scope = services.CreateAsyncScope();
        EstablishTestContext(scope.ServiceProvider, tenant);
        return scope;
    }

    private static async global::System.Threading.Tasks.Task CreateItemAsync(
        ServiceProvider services,
        string tenant,
        Guid id,
        string name,
        decimal price
    )
    {
        await using var scope = Operation(services, tenant);
        var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<CatalogChange.Changed>(
            await scope
                .ServiceProvider.GetRequiredService<ICatalogCommands>()
                .CreateAsync(id, name, price, Token)
        );
        Assert.DoesNotContain(await ReadAsync(services, tenant), row => row.Id == id);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static async global::System.Threading.Tasks.Task WithDatabaseAsync(
        Func<ServiceProvider, string, global::System.Threading.Tasks.Task> body
    )
    {
        string admin =
            Environment.GetEnvironmentVariable("CATALOG_TEST_ADMIN_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set CATALOG_TEST_ADMIN_CONNECTION_STRING.");
        string name = "patterns_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync(Token);
        await ExecuteAsync(connection, $"CREATE DATABASE \"{name}\"");
        string target = new NpgsqlConnectionStringBuilder(admin)
        {
            Database = name,
            Pooling = false,
        }.ConnectionString;
        try
        {
            await RunHostAsync(target, "migrate");
            using var services = ConsumerRoot
                .Host.Composition.CreateServices(target)
                .BuildServiceProvider(
                    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
                );
            await body(services, target);
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP DATABASE \"{name}\" WITH (FORCE)");
        }
    }
}
