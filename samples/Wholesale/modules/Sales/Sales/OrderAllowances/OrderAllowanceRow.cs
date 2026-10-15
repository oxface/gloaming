using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Sales.OrderAllowances;

internal sealed class OrderAllowanceRow
{
    public required string OrganizationKey { get; set; }
    public Guid CustomerId { get; set; }
    public required string Currency { get; set; }
    public decimal Ceiling { get; set; }
    public decimal Reserved { get; set; }
    public long Version { get; set; }

    internal OrderAllowanceAggregate ToAggregate() =>
        new(CustomerId, Money.Create(Ceiling, Currency), Money.Create(Reserved, Currency), Version);

    internal static void Configure(
        ModelBuilder model,
        System.Linq.Expressions.Expression<Func<string>> owner
    )
    {
        var row = model.Entity<OrderAllowanceRow>();
        row.ToTable(
            "customer_order_allowances",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_order_allowance_amounts",
                    "ceiling > 0 AND reserved >= 0 AND reserved <= ceiling"
                );
                table.HasCheckConstraint("ck_order_allowance_version", "version > 0");
            }
        );
        row.HasKey(value => new
        {
            value.OrganizationKey,
            value.CustomerId,
            value.Currency,
        });
        row.Property(value => value.OrganizationKey)
            .HasColumnName("organization_key")
            .HasMaxLength(256);
        row.Property(value => value.CustomerId).HasColumnName("customer_id").ValueGeneratedNever();
        row.Property(value => value.Currency).HasColumnName("currency").HasMaxLength(3);
        row.Property(value => value.Ceiling).HasColumnName("ceiling");
        row.Property(value => value.Reserved).HasColumnName("reserved");
        row.Property(value => value.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        row.HasOne<CustomerRow>()
            .WithMany()
            .HasForeignKey(value => new { value.OrganizationKey, value.CustomerId })
            .HasPrincipalKey(value => new { value.OrganizationKey, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        row.HasTenantOwnership(value => value.OrganizationKey, owner, "OrganizationScope");
    }
}
