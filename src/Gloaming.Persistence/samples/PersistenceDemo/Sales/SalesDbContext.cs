using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.PersistenceDemo.Sales;

public sealed class SalesDbContext(
    DbContextOptions<SalesDbContext> options,
    ITenantContextAccessor tenancy
) : DbContext(options)
{
    public DbSet<CustomerReference> Customers => Set<CustomerReference>();
    public DbSet<CustomerAddressReference> CustomerAddresses => Set<CustomerAddressReference>();
    private string RequiredOrganizationKey => tenancy.Current.RequireTenant().Value;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sales");
        var customer = modelBuilder.Entity<CustomerReference>();
        customer.ToTable("customer_reference");
        customer.HasKey(row => row.Id);
        customer.HasAlternateKey(row => new { row.OrganizationKey, row.Id });
        customer.Property(row => row.Id).ValueGeneratedNever();
        customer.Property(row => row.Code).IsRequired();
        customer.Property(row => row.DisplayName).IsRequired();
        customer
            .Property(row => row.Version)
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        customer.HasIndex(row => new { row.OrganizationKey, row.Code }).IsUnique();
        customer.HasTenantOwnership(
            row => row.OrganizationKey,
            () => RequiredOrganizationKey,
            "OrganizationScope"
        );

        var address = modelBuilder.Entity<CustomerAddressReference>();
        address.ToTable("customer_address_reference");
        address.HasKey(row => row.Id);
        address.Property(row => row.Id).ValueGeneratedNever();
        address.Property(row => row.AddressLine).IsRequired();
        address.HasTenantOwnership(
            row => row.OrganizationKey,
            () => RequiredOrganizationKey,
            "OrganizationScope"
        );
        address
            .HasOne<CustomerReference>()
            .WithMany()
            .HasForeignKey(row => new { row.OrganizationKey, row.CustomerId })
            .HasPrincipalKey(row => new { row.OrganizationKey, row.Id })
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_customer_address_reference_customer_tenant");
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
