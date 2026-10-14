using System.Linq.Expressions;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

internal static class HistoryMapping
{
    internal static void Configure(
        ModelBuilder model,
        Expression<Func<string>> requiredOrganization
    )
    {
        var stream = model.Entity<EventStream>();
        var stored = model.Entity<StoredEvent>();
        stream
            .Property(row => row.OrganizationKey)
            .HasColumnName("organization_key")
            .HasMaxLength(256);
        stored
            .Property(row => row.OrganizationKey)
            .HasColumnName("organization_key")
            .HasMaxLength(256);
        model.ConfigureEventSourcingStorage<EventStream, StoredEvent>(
            row => new { row.OrganizationKey, row.Id },
            row => new { row.OrganizationKey, row.EventId },
            row => new { row.OrganizationKey, row.StreamId }
        );
        stream.HasTenantOwnership(
            row => row.OrganizationKey,
            requiredOrganization,
            "OrganizationScope"
        );
        stored.HasTenantOwnership(
            row => row.OrganizationKey,
            requiredOrganization,
            "OrganizationScope"
        );
        stored.Property(row => row.Payload).HasColumnType("jsonb");
    }
}
