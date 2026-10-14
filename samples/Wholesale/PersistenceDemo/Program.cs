using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.PersistenceDemo;
using ModulithFoundry.Samples.Wholesale.PersistenceDemo.Inventory;
using ModulithFoundry.Samples.Wholesale.PersistenceDemo.Sales;

string connection =
    Environment.GetEnvironmentVariable("WHOLESALE_DEMO_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set WHOLESALE_DEMO_CONNECTION_STRING to a disposable demo database."
    );
await using var provider = DemoComposition
    .CreateServices(connection)
    .BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
    );

// Consumer-controlled module order and initialization; no transaction spans both modules.
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
    StockReference? reference = await inventory.Stock.SingleOrDefaultAsync(row =>
        row.Sku == "WIDGET"
    );
    if (reference is null)
    {
        reference = new StockReference
        {
            Id = Guid.NewGuid(),
            OrganizationKey = organization,
            Sku = "WIDGET",
            Quantity = quantity,
        };
        inventory.Stock.Add(reference);
        await inventory.SaveChangesAsync();
    }
    Console.WriteLine($"{organization}: WIDGET availability={reference.Quantity}");

    var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
    CustomerReference? customer = await sales.Customers.SingleOrDefaultAsync(row =>
        row.Code == "BUYER"
    );
    if (customer is null)
    {
        customer = new CustomerReference
        {
            Id = Guid.NewGuid(),
            OrganizationKey = organization,
            Code = "BUYER",
            DisplayName = organization == "wholesale-alpha" ? "Alpha Draft" : "Beta Draft",
            Version = 1,
        };
        sales.Customers.Add(customer);
        await sales.SaveChangesAsync();
    }

    CustomerAddressReference? address = await sales.CustomerAddresses.SingleOrDefaultAsync(row =>
        row.CustomerId == customer.Id
    );
    if (address is null)
    {
        address = new CustomerAddressReference
        {
            Id = Guid.NewGuid(),
            OrganizationKey = organization,
            CustomerId = customer.Id,
            AddressLine = "Draft address",
        };
        sales.CustomerAddresses.Add(address);
        await sales.SaveChangesAsync();
    }
    string desiredName = organization == "wholesale-alpha" ? "Alpha Retail" : "Beta Retail";
    string desiredAddress = organization == "wholesale-alpha" ? "42 Market Street" : "7 Dock Road";
    if (customer.DisplayName != desiredName || address.AddressLine != desiredAddress)
    {
        await using var transaction = await sales.Database.BeginTransactionAsync();
        try
        {
            await CustomerProfileChanges.ApplyAsync(
                sales,
                new CustomerProfileChange(
                    customer.Id,
                    address.Id,
                    customer.Version,
                    desiredName,
                    desiredAddress
                )
            );
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
    Console.WriteLine($"{organization}: BUYER customer={customer.DisplayName}");
    Console.WriteLine($"{organization}: BUYER address={address.AddressLine}");
    Console.WriteLine($"{organization}: BUYER profile-version={customer.Version}");
}
