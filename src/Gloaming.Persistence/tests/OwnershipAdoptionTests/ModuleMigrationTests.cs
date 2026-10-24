using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.PersistenceDemo.Inventory;
using Gloaming.Samples.Wholesale.PersistenceDemo.Sales;
using Gloaming.Tenancy;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.PersistenceDemo.Tests;

public sealed class ModuleMigrationTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private const string Alpha = "wholesale-alpha";
    private const string Beta = "wholesale-beta";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EitherModuleCanMigrateFirstAndTheSecondPreservesItsData(bool inventoryFirst)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using ServiceProvider provider = Provider(connection);
        string first = inventoryFirst ? "inventory" : "sales";
        string[] firstTables = inventoryFirst
            ?
            [
                "inventory.__EFMigrationsHistory",
                "inventory.reference_category",
                "inventory.stock_reference",
            ]
            :
            [
                "sales.__EFMigrationsHistory",
                "sales.customer_address_reference",
                "sales.customer_reference",
            ];
        await using (AsyncServiceScope setup = provider.CreateAsyncScope())
            await Context(setup, first).Database.MigrateAsync(Token);
        Assert.Equal(firstTables, await TablesAsync(connection));

        Guid witnessId = Guid.NewGuid();
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            DbContext context = Context(scope, first);
            if (inventoryFirst)
                context.Add(
                    new StockReference
                    {
                        Id = witnessId,
                        OrganizationKey = Alpha,
                        Sku = "WIDGET",
                        Quantity = 42,
                    }
                );
            else
                context.Add(Customer(witnessId, Alpha, "Alpha Retail"));
            await context.SaveChangesAsync(Token);
        }

        await using (AsyncServiceScope setup = provider.CreateAsyncScope())
            await Context(setup, inventoryFirst ? "sales" : "inventory")
                .Database.MigrateAsync(Token);
        Assert.Equal(
            [
                "inventory.__EFMigrationsHistory",
                "inventory.reference_category",
                "inventory.stock_reference",
                "sales.__EFMigrationsHistory",
                "sales.customer_address_reference",
                "sales.customer_reference",
            ],
            await TablesAsync(connection)
        );
        await using (AsyncServiceScope alpha = Scope(provider, Alpha))
        {
            var inventory = alpha.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var sales = alpha.ServiceProvider.GetRequiredService<SalesDbContext>();
            if (inventoryFirst)
            {
                Assert.Equal(42, (await inventory.Stock.SingleAsync(Token)).Quantity);
                sales.Customers.Add(Customer(Guid.NewGuid(), Alpha, "Alpha Retail"));
                await sales.SaveChangesAsync(Token);
            }
            else
            {
                Assert.Equal(
                    "Alpha Retail",
                    (await sales.Customers.SingleAsync(Token)).DisplayName
                );
                inventory.Stock.Add(
                    new StockReference
                    {
                        Id = Guid.NewGuid(),
                        OrganizationKey = Alpha,
                        Sku = "WIDGET",
                        Quantity = 42,
                    }
                );
                await inventory.SaveChangesAsync(Token);
            }
            Assert.Equal(
                witnessId,
                inventoryFirst
                    ? (await inventory.Stock.SingleAsync(Token)).Id
                    : (await sales.Customers.SingleAsync(Token)).Id
            );
            foreach (DbContext context in new DbContext[] { inventory, sales })
                Assert.Equal(
                    context.GetService<IMigrationsAssembly>().Migrations.Keys,
                    await context.Database.GetAppliedMigrationsAsync(Token)
                );
        }
        await using (AsyncServiceScope beta = Scope(provider, Beta))
        {
            var sales = beta.ServiceProvider.GetRequiredService<SalesDbContext>();
            Assert.Empty(await sales.Customers.ToArrayAsync(Token));
            sales.Customers.Add(Customer(Guid.NewGuid(), Beta, "Beta Retail"));
            await sales.SaveChangesAsync(Token);
            Assert.Equal("Beta Retail", (await sales.Customers.SingleAsync(Token)).DisplayName);
        }
        await using AsyncServiceScope freshAlpha = Scope(provider, Alpha);
        Assert.Equal(
            "Alpha Retail",
            (
                await freshAlpha
                    .ServiceProvider.GetRequiredService<SalesDbContext>()
                    .Customers.SingleAsync(Token)
            ).DisplayName
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SalesWiresOwnershipValidationIntoBothSaveOverrides(bool asynchronous)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using ServiceProvider provider = Provider(connection);
        await using (AsyncServiceScope setup = provider.CreateAsyncScope())
            await Context(setup, "sales").Database.MigrateAsync(Token);
        await using (AsyncServiceScope alpha = Scope(provider, Alpha))
        {
            var sales = alpha.ServiceProvider.GetRequiredService<SalesDbContext>();
            sales.Customers.Add(Customer(Guid.NewGuid(), Beta, "Foreign"));
            var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                asynchronous
                    ? sales.SaveChangesAsync(false, Token)
                    : Task.FromResult(sales.SaveChanges(false))
            );
            Assert.Equal(TenantOwnershipFailure.ForeignOwner, failure.Reason);
        }
        await using AsyncServiceScope beta = Scope(provider, Beta);
        Assert.Empty(
            await beta
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Customers.ToArrayAsync(Token)
        );
    }

    private static ServiceProvider Provider(string connection) =>
        DemoComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

    private static AsyncServiceScope Scope(ServiceProvider provider, string organization)
    {
        AsyncServiceScope scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        return scope;
    }

    private static DbContext Context(AsyncServiceScope scope, string module) =>
        module == "inventory"
            ? scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            : scope.ServiceProvider.GetRequiredService<SalesDbContext>();

    private static CustomerReference Customer(Guid id, string organization, string name) =>
        new()
        {
            Id = id,
            OrganizationKey = organization,
            Code = "BUYER",
            DisplayName = name,
            Version = 1,
        };

    private static async Task<string[]> TablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            """
            SELECT table_schema || '.' || table_name
            FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
              AND table_type = 'BASE TABLE'
            ORDER BY table_schema, table_name
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(Token);
        var tables = new List<string>();
        while (await reader.ReadAsync(Token))
            tables.Add(reader.GetString(0));
        return tables.ToArray();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
