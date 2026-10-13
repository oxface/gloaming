using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rootbolt.Persistence.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Inventory.Reservations;

internal static class StockReservationMapping
{
    internal static void Configure(
        EntityTypeBuilder<StockReservation> reservation,
        Expression<Func<string>> organization
    )
    {
        reservation.ToTable(
            "stock_reservations",
            table =>
                table.HasCheckConstraint(
                    "valid_reservation",
                    "\"Quantity\" > 0 AND \"ExpectedVersion\" >= 1 AND \"Version\" >= 1 AND \"Decision\" BETWEEN 1 AND 4 AND (NOT \"Released\" OR \"Decision\" = 1)"
                )
        );
        reservation.HasKey(row => new { row.OrganizationKey, row.Id });
        reservation.Property(row => row.Id).ValueGeneratedNever();
        reservation.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        reservation.HasTenantOwnership(
            row => row.OrganizationKey,
            organization,
            "OrganizationScope"
        );
        reservation.Property(row => row.OrganizationKey).HasMaxLength(256);
    }
}
