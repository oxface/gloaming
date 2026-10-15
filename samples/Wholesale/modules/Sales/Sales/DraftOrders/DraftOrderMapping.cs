using System.Linq.Expressions;
using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;

internal static class DraftOrderMapping
{
    internal static void Configure(
        ModelBuilder model,
        Expression<Func<string>> requiredOrganization
    )
    {
        var root = model.Entity<DraftOrderRow>();
        root.ToTable(
            "draft_orders",
            table =>
            {
                table.HasCheckConstraint("ck_draft_order_version", "version > 0");
                table.HasCheckConstraint("ck_draft_order_limit", "total_limit > 0");
            }
        );
        root.HasKey(row => new { row.OrganizationKey, row.Id }).HasName("pk_draft_orders");
        root.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        root.Property(row => row.OrganizationKey)
            .HasColumnName("organization_key")
            .HasMaxLength(256);
        root.Property(row => row.CustomerId).HasColumnName("customer_id");
        root.Property(row => row.Code).HasColumnName("code").HasMaxLength(128);
        root.Property(row => row.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        root.Property(row => row.Currency).HasColumnName("currency").HasMaxLength(3);
        root.Property(row => row.IsSubmitted).HasColumnName("is_submitted");
        root.Property(row => row.TotalLimit).HasColumnName("total_limit");
        root.HasIndex(row => new { row.OrganizationKey, row.Code })
            .IsUnique()
            .HasDatabaseName("ux_draft_orders_code");
        root.HasOne<CustomerRow>()
            .WithMany()
            .HasForeignKey(row => new { row.OrganizationKey, row.CustomerId })
            .HasPrincipalKey(row => new { row.OrganizationKey, row.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_draft_orders_customer");
        root.HasTenantOwnership(
            row => row.OrganizationKey,
            requiredOrganization,
            "OrganizationScope"
        );

        var child = model.Entity<DraftOrderLineRow>();
        child.ToTable(
            "draft_order_lines",
            table =>
            {
                table.HasCheckConstraint("ck_draft_line_quantity", "quantity > 0");
                table.HasCheckConstraint("ck_draft_line_price", "unit_price >= 0");
            }
        );
        child
            .HasKey(row => new
            {
                row.OrganizationKey,
                row.OrderId,
                row.Id,
            })
            .HasName("pk_draft_order_lines");
        child.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        child.Property(row => row.OrderId).HasColumnName("order_id");
        child
            .Property(row => row.OrganizationKey)
            .HasColumnName("organization_key")
            .HasMaxLength(256);
        child.Property(row => row.Sku).HasColumnName("sku").HasMaxLength(128);
        child.Property(row => row.Quantity).HasColumnName("quantity");
        child.Property(row => row.UnitPrice).HasColumnName("unit_price");
        root.HasMany(row => row.Lines)
            .WithOne()
            .HasForeignKey(row => new { row.OrganizationKey, row.OrderId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_draft_order_lines_order");
        child.HasTenantOwnership(
            row => row.OrganizationKey,
            requiredOrganization,
            "OrganizationScope"
        );
    }
}
