using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;
using Rootbolt.Auditing.EntityFrameworkCore;
using Rootbolt.Auditing.EntityFrameworkCore.Postgres;
using Rootbolt.EventSourcing.EntityFrameworkCore;
using Rootbolt.Messaging.EntityFrameworkCore;
using Rootbolt.Messaging.EntityFrameworkCore.Postgres;
using Rootbolt.Persistence.EntityFrameworkCore;
using Rootbolt.Tenancy;

namespace ModulithFoundry.Samples.Wholesale.Inventory;

public sealed class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options,
    ITenantContextAccessor tenancy
) : DbContext(options)
{
    internal DbSet<EventStream> EventStreams => Set<EventStream>();
    internal DbSet<StoredEvent> Events => Set<StoredEvent>();
    internal DbSet<StockRow> Stock => Set<StockRow>();
    internal DbSet<StockPositionStateRow> StockPositions => Set<StockPositionStateRow>();
    internal string RequiredOrganizationKey => tenancy.Current.RequireTenant().Value;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");
        Reservations.StockReservationMapping.Configure(
            modelBuilder.Entity<Reservations.StockReservation>(),
            () => RequiredOrganizationKey
        );
        var audit = modelBuilder.ConfigurePostgresAudit("inventory", "audit_entries");
        audit.HasTenantOwnership(
            row => row.TenantKey!,
            () => RequiredOrganizationKey,
            "OrganizationScope"
        );
        HistoryMapping.Configure(modelBuilder, () => RequiredOrganizationKey);
        InlineStateMapping.Configure(modelBuilder, () => RequiredOrganizationKey);
        modelBuilder
            .ConfigurePostgresInbox("inventory", "inbox_messages")
            .HasTenantOwnership(
                row => row.TenantKey!,
                () => RequiredOrganizationKey,
                "OrganizationScope"
            );
        modelBuilder
            .ConfigurePostgresOutbox("inventory", "outbox_messages")
            .HasTenantOwnership(
                row => row.TenantKey!,
                () => RequiredOrganizationKey,
                "OrganizationScope"
            );
        var stock = modelBuilder.Entity<StockRow>();
        stock.ToTable("stock_availability");
        stock.HasKey(row => row.Id);
        stock.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        stock.Property(row => row.Sku).HasColumnName("sku").HasMaxLength(128);
        stock.Property(row => row.AvailableQuantity).HasColumnName("available_quantity");
        stock.HasIndex(row => new { row.OrganizationKey, row.Sku }).IsUnique();
        stock.HasTenantOwnership(
            row => row.OrganizationKey,
            () => RequiredOrganizationKey,
            "OrganizationScope"
        );
        stock
            .Property(row => row.OrganizationKey)
            .HasColumnName("organization_key")
            .HasMaxLength(256);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateTenantChanges(() => RequiredOrganizationKey);
        this.ValidateAuditChanges();
        this.ValidateEventStreamChanges();
        this.ValidateOutboxChanges();
        this.ValidateInboxChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateTenantChanges(() => RequiredOrganizationKey);
        this.ValidateAuditChanges();
        this.ValidateEventStreamChanges();
        this.ValidateOutboxChanges();
        this.ValidateInboxChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
