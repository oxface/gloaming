using Gloaming.Samples.Wholesale.PersistenceDemo;
using Gloaming.Samples.Wholesale.PersistenceDemo.Inventory;
using Gloaming.Samples.Wholesale.PersistenceDemo.Sales;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

string connection =
    Environment.GetEnvironmentVariable("WHOLESALE_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set WHOLESALE_DEMO_CONNECTION_STRING to a disposable database."
    );
await using var provider = DemoComposition
    .CreateServices(connection)
    .BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
    );

// The application explicitly owns migration order; each module has its own history.
await using (var setup = provider.CreateAsyncScope())
{
    await setup.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
    await setup.ServiceProvider.GetRequiredService<SalesDbContext>().Database.MigrateAsync();
}

foreach (var (organization, quantity) in new[] { ("wholesale-alpha", 42), ("wholesale-beta", 7) })
{
    await using var scope = provider.CreateAsyncScope();
    scope
        .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
        .Initialize(TenantContext.ForTenant(new TenantId(organization)));
    var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await using (var transaction = await inventory.Database.BeginTransactionAsync())
    {
        if (!await inventory.Stock.AnyAsync(row => row.Sku == "WIDGET"))
            inventory.Stock.Add(
                new StockReference
                {
                    Id = Guid.NewGuid(),
                    OrganizationKey = organization,
                    Sku = "WIDGET",
                    Quantity = quantity,
                }
            );

        await inventory.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    var stock = await inventory.Stock.AsNoTracking().SingleAsync(row => row.Sku == "WIDGET");
    Console.WriteLine($"{organization}: WIDGET availability={stock.Quantity}");

    // This second transaction belongs to Sales; no transaction spans the two contexts.
    var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
    await using (var transaction = await sales.Database.BeginTransactionAsync())
    {
        if (!await sales.Customers.AnyAsync(row => row.Code == "BUYER"))
            sales.Customers.Add(
                new CustomerReference
                {
                    Id = Guid.NewGuid(),
                    OrganizationKey = organization,
                    Code = "BUYER",
                    DisplayName =
                        organization == "wholesale-alpha" ? "Alpha Retail" : "Beta Retail",
                    Version = 1,
                }
            );

        await sales.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    var customer = await sales.Customers.AsNoTracking().SingleAsync(row => row.Code == "BUYER");
    Console.WriteLine($"{organization}: BUYER customer={customer.DisplayName}");
}
