using System.Linq.Expressions;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ModulithFoundry.Samples.Wholesale.Sales;

public static class SalesReservationReplyDatabase
{
    /// <summary>Selects the finite reservation-reply model; roles/policies and admitted scopes remain host-owned.</summary>
    public static IServiceCollection AddSalesReservationReplyDatabase(
        this IServiceCollection services,
        string connection
    )
    {
        services.AddSalesDatabase(connection, useRowSecurity: true);
        // Native EF applies later configuration last. Reuse the registered capabilities/interceptor
        // while selecting this consumer model instead of the separate HTTP six-table model.
        return services.ConfigureDbContext<SalesDbContext>(options =>
            options.ReplaceService<IModelCustomizer, ReservationReplyRowSecurityModelCustomizer>()
        );
    }
}

internal sealed class ReservationReplyRowSecurityModelCustomizer(
    ModelCustomizerDependencies dependencies
) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        var sales = (SalesDbContext)context;
        var organization = Expression.Lambda<Func<string>>(
            Expression.Property(
                Expression.Constant(sales),
                nameof(SalesDbContext.RequiredOrganizationKey)
            )
        );
        modelBuilder
            .Entity<WorkflowInstanceRecord>()
            .HasTenantOwnership(row => row.TenantKey!, organization, "OrganizationScope")
            .WithRls("app.tenant_key");
        modelBuilder.Entity<InboxMessageRecord>().WithRls("app.tenant_key");
    }
}
