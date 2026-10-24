using System.Linq.Expressions;
using Gloaming.ActorIdentity;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Sales.DraftOrders;

internal sealed class DraftOrderGrantRow
{
    public required string OrganizationKey { get; init; }
    public Guid OrderId { get; init; }
    public ActorKind SubjectKind { get; init; }
    public required string SubjectKey { get; init; }
    public DraftOrderAction Permission { get; init; }

    internal static void Configure(ModelBuilder model, Expression<Func<string>> organization)
    {
        var grant = model.Entity<DraftOrderGrantRow>();
        grant.ToTable(
            "draft_order_grants",
            table =>
            {
                table.HasCheckConstraint("ck_draft_grant_subject", "subject_kind IN (2, 3)");
                table.HasCheckConstraint("ck_draft_grant_permission", "permission IN (1, 2)");
                table.HasCheckConstraint("ck_draft_grant_key", "subject_key ~ '[^[:space:]]'");
            }
        );
        grant.HasKey(row => new
        {
            row.OrganizationKey,
            row.OrderId,
            row.SubjectKind,
            row.SubjectKey,
            row.Permission,
        });
        grant
            .Property(row => row.OrganizationKey)
            .HasColumnName("organization_key")
            .HasMaxLength(256);
        grant.Property(row => row.OrderId).HasColumnName("order_id");
        grant.Property(row => row.SubjectKind).HasColumnName("subject_kind").HasConversion<int>();
        grant.Property(row => row.SubjectKey).HasColumnName("subject_key").HasMaxLength(256);
        grant.Property(row => row.Permission).HasColumnName("permission").HasConversion<int>();
        grant.HasIndex(row => new
        {
            row.OrganizationKey,
            row.SubjectKind,
            row.SubjectKey,
            row.OrderId,
        });
        grant
            .HasOne<DraftOrderRow>()
            .WithMany()
            .HasForeignKey(row => new { row.OrganizationKey, row.OrderId })
            .OnDelete(DeleteBehavior.Restrict);
        grant.HasTenantOwnership(row => row.OrganizationKey, organization, "OrganizationScope");
    }
}
