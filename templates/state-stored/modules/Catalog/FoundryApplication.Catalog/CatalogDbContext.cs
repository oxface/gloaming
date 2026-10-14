using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ConsumerRoot.Catalog;

public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options,
    ITenantContextAccessor tenancy
) : DbContext(options)
{
    internal DbSet<ItemRow> Items => Set<ItemRow>();
    private string RequiredTenantKey => tenancy.Current.RequireTenant().Value;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");
        var item = modelBuilder.Entity<ItemRow>();
        item.ToTable("items");
        item.HasKey(row => row.Id);
        item.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        item.Property(row => row.Name).HasColumnName("name").HasMaxLength(200);
        item.Property(row => row.TenantKey).HasColumnName("tenant_key").HasMaxLength(200);
        item.HasTenantOwnership(row => row.TenantKey, () => RequiredTenantKey, "TenantScope");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateTenantChanges(() => RequiredTenantKey);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override global::System.Threading.Tasks.Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateTenantChanges(() => RequiredTenantKey);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
