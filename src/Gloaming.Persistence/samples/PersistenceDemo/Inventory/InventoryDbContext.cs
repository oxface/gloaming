using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Inventory;

public sealed class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options,
    ITenantContextAccessor tenancy
) : DbContext(options)
{
    public DbSet<StockReference> Stock => Set<StockReference>();
    public DbSet<ReferenceCategory> Categories => Set<ReferenceCategory>();
    private string RequiredOrganizationKey => tenancy.Current.RequireTenant().Value;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");
        var stock = modelBuilder.Entity<StockReference>();
        stock.ToTable("stock_reference");
        stock.HasKey(row => row.Id);
        stock.Property(row => row.Id).ValueGeneratedNever();
        stock.Property(row => row.Sku).IsRequired();
        stock.HasIndex(row => new { row.OrganizationKey, row.Sku }).IsUnique();
        stock.HasQueryFilter("SoftDeletion", row => !row.IsDeleted);
        stock.HasTenantOwnership(
            row => row.OrganizationKey,
            () => RequiredOrganizationKey,
            "OrganizationScope"
        );

        var category = modelBuilder.Entity<ReferenceCategory>();
        category.ToTable("reference_category");
        category.HasKey(row => row.Id);
        category.Property(row => row.Id).ValueGeneratedNever();
        category.Property(row => row.Name).IsRequired();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateTenantChanges(() => RequiredOrganizationKey);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateTenantChanges(() => RequiredOrganizationKey);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
