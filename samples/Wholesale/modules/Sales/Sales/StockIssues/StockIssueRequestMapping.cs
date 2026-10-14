using System.Linq.Expressions;
using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModulithFoundry.Samples.Wholesale.Sales.StockIssues;

internal static class StockIssueRequestMapping
{
    internal static void Configure(
        EntityTypeBuilder<StockIssueRequestRow> request,
        Expression<Func<string>> organization
    )
    {
        request.ToTable(
            "stock_issue_requests",
            table =>
            {
                table.HasCheckConstraint(
                    "valid_request",
                    "\"Quantity\" > 0 AND \"ExpectedStockVersion\" >= 1 AND \"Version\" >= 1 AND \"Status\" BETWEEN 1 AND 4"
                );
            }
        );
        request.HasKey(row => new { row.OrganizationKey, row.Id });
        request.Property(row => row.Id).ValueGeneratedNever();
        request.Property(row => row.CommandMessageId).ValueGeneratedNever();
        request.HasIndex(row => row.CommandMessageId).IsUnique();
        request.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        request.HasIndex(row => new
        {
            row.OrganizationKey,
            row.Status,
            row.ReplyDeadline,
        });
        request.HasTenantOwnership(row => row.OrganizationKey, organization, "OrganizationScope");
        request.Property(row => row.OrganizationKey).HasMaxLength(256);
    }
}
