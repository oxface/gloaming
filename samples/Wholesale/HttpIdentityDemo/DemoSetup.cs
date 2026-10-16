using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Access;
using ModulithFoundry.Samples.Wholesale.Access.Persistence;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo;

// Finite disposable-demo setup. Native saves and transactions are visible here.
public static class DemoSetup
{
    public static async Task InitializeAsync(
        string connection,
        bool fullDemo,
        CancellationToken cancellationToken,
        DemoAccessIdentities? identities = null
    )
    {
        var accessOptions = new DbContextOptionsBuilder<AccessDbContext>();
        AccessDatabase.Configure(accessOptions, connection);
        await using (var access = new AccessDbContext(accessOptions.Options))
        {
            await access.Database.MigrateAsync(cancellationToken);
            if (fullDemo)
            {
                var migrationOptions = new DbContextOptionsBuilder<InventoryDbContext>();
                InventoryDatabase.Configure(migrationOptions, connection);
                using (var migrationContext = new TenantContextAccessor())
                await using (
                    var inventory = new InventoryDbContext(
                        migrationOptions.Options,
                        migrationContext
                    )
                )
                    await inventory.Database.MigrateAsync(cancellationToken);
            }
            if (fullDemo)
            {
                var salesOptions = new DbContextOptionsBuilder<SalesDbContext>();
                SalesDatabase.Configure(salesOptions, connection);
                using var migrationTenant = new TenantContextAccessor();
                await using var sales = new SalesDbContext(salesOptions.Options, migrationTenant);
                await sales.Database.MigrateAsync(cancellationToken);
            }
            await using var transaction = await access.Database.BeginTransactionAsync(
                cancellationToken
            );
            AccessDemoSeed.Stage(access, identities);
            await access.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        if (!fullDemo)
            return;
        var options = new DbContextOptionsBuilder<InventoryDbContext>();
        InventoryDatabase.Configure(options, connection);
        foreach (
            var (organization, quantity) in new[] { ("wholesale-alpha", 42), ("wholesale-beta", 7) }
        )
        {
            using var tenant = new TenantContextAccessor();
            tenant.Initialize(TenantContext.ForTenant(new TenantId(organization)));
            await using var inventory = new InventoryDbContext(options.Options, tenant);
            await using var transaction = await inventory.Database.BeginTransactionAsync(
                cancellationToken
            );
            InventoryDemoSeed.Stage(inventory, quantity);
            await inventory.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var salesOptions = new DbContextOptionsBuilder<SalesDbContext>();
            SalesDatabase.Configure(salesOptions, connection);
            await using var sales = new SalesDbContext(salesOptions.Options, tenant);
            await using var salesTransaction = await sales.Database.BeginTransactionAsync(
                cancellationToken
            );
            SalesDemoSeed.Stage(sales);
            DraftOrderAccessDemoSeed.Stage(sales);
            await sales.SaveChangesAsync(cancellationToken);
            await salesTransaction.CommitAsync(cancellationToken);
        }
    }
}
