using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo;

/// <summary>Editable composition; module contexts remain distinct even when hosted together.</summary>
public static class WorkflowComposition
{
    public static IServiceCollection AddWorkflowDemo(
        this IServiceCollection services,
        string salesConnection,
        string inventoryConnection
    )
    {
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddSalesDatabase(salesConnection);
        services.AddInventoryDatabase(inventoryConnection);
        services.AddStockIssueRequests();
        services.AddStockReservationOrders();
        services.AddStockPositionCommands();
        services.AddStockPositionQueries();
        services.AddStockPositionHistory();
        services.AddStockIssueInbox();
        services.AddStockReservationInbox();
        return services;
    }
}
