using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.LocalWorkflowDemo.Delivery;
using ModulithFoundry.Samples.Wholesale.Sales;

namespace ModulithFoundry.Samples.Wholesale.LocalWorkflowDemo;

public static class LocalWorkflowComposition
{
    public static IServiceCollection AddLocalReservationDemo(
        this IServiceCollection services,
        string salesConnection,
        string inventoryConnection,
        TimeSpan? leaseDuration = null
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
        services.AddStockReservationOrders();
        services.AddStockPositionCommands();
        services.AddStockPositionQueries();
        services.AddStockPositionHistory();
        services.AddStockReservationInbox();
        services.AddStockReservationIntake();
        services.AddStockReservationOutcomeIntake();
        var options = new OutboxDispatchOptions(
            leaseDuration ?? TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(100)
        );
        services.AddPostgresOutboxDispatcher<SalesDbContext, ReservationCommandPublisher>(options);
        services.AddPostgresOutboxDispatcher<InventoryDbContext, ReservationOutcomePublisher>(
            options
        );
        return services;
    }

    public static IServiceCollection AddLocalReservationWorkers(this IServiceCollection services)
    {
        var outbox = new OutboxWorkerOptions(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100)
        );
        var inbox = new InboxWorkerOptions(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100)
        );
        services.AddOutboxWorker<SalesDbContext>(outbox);
        services.AddOutboxWorker<InventoryDbContext>(outbox);
        services.AddInboxWorker<InventoryDbContext>(StockReservationAdmission.Subscription, inbox);
        services.AddInboxWorker<SalesDbContext>(StockReservationReplyAdmission.Subscription, inbox);
        return services;
    }
}
