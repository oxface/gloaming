using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Worker.Local.Delivery;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.Worker.Local;

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
        services.AddPostgresOutboxDispatcher<SalesDbContext, ReservationCommandPublisher>(
            options with
            {
                RouteKeys = ["inventory.reserve-stock", "inventory.release-reservation"],
            }
        );
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
