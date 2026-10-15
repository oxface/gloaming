using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

namespace ModulithFoundry.Samples.Wholesale.Purchasing;

public sealed class PurchasingDbContext(
    DbContextOptions<PurchasingDbContext> options,
    ITenantContextAccessor tenancy
) : DbContext(options)
{
    internal DbSet<EventStream> EventStreams => Set<EventStream>();
    internal DbSet<StoredEvent> Events => Set<StoredEvent>();
    internal DbSet<PurchaseOrderStateRow> PurchaseOrders => Set<PurchaseOrderStateRow>();
    internal string RequiredOrganizationKey => tenancy.Current.RequireTenant().Value;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("purchasing");
        HistoryMapping.Configure(modelBuilder, () => RequiredOrganizationKey);
        InlineStateMapping.Configure(modelBuilder, () => RequiredOrganizationKey);
    }
}
