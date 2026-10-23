using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;
using Gloaming.EntityFrameworkCore;
using Gloaming.EventReactions.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ModulithFoundry.Samples.Wholesale.Sales;

public static class SalesDatabase
{
    public static IServiceCollection AddSalesDatabase(
        this IServiceCollection services,
        string connection,
        bool useRowSecurity = false
    )
    {
        services.AddTenantOwnershipValidation<SalesDbContext, string>(context =>
            context.RequiredOrganizationKey
        );
        services.AddAuditValidation<SalesDbContext>();
        services.AddPostgresCommandReceipts<SalesDbContext>();
        services.AddInboxValidation<SalesDbContext>();
        services.AddOutboxValidation<SalesDbContext>();
        services.AddWorkflowValidation<SalesDbContext>();
        services.AddEventReactions<SalesDbContext, SalesModule>(events =>
            events.AddReaction<
                DraftOrders.DraftSubmitted,
                OrderAllowances.ReserveCustomerAllowance
            >()
        );
        if (useRowSecurity)
            services.AddPostgresTenantRowSecurity<SalesDbContext>();

        return services.AddDbContext<SalesDbContext>(
            (provider, options) =>
            {
                Configure(options, connection);
                if (useRowSecurity)
                {
                    options.ReplaceService<IModelCustomizer, SalesRowSecurityModelCustomizer>();
                    options.UseRls<SalesDbContext>(provider);
                }
                else
                    options.ConfigureGloamingCapabilities<SalesDbContext>(provider);
            }
        );
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "sales")
        );
}
