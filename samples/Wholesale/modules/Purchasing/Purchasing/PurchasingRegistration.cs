using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

namespace ModulithFoundry.Samples.Wholesale.Purchasing;

public static class PurchasingRegistration
{
    public static IServiceCollection AddPurchaseOrderHistory(this IServiceCollection services) =>
        RegisterHistory(services);

    private static IServiceCollection RegisterHistory(IServiceCollection services)
    {
        services.TryAddScoped<PurchaseOrderHistoryReader>();
        services.TryAddScoped<IPurchaseOrderHistory>(provider =>
            provider.GetRequiredService<PurchaseOrderHistoryReader>()
        );
        return services;
    }

    private static void RegisterStore(IServiceCollection services)
    {
        services.AddEventStore<PurchaseOrderAggregate, PurchaseOrderStore>();
    }

    public static IServiceCollection AddPurchaseOrderRebuilding(this IServiceCollection services)
    {
        RegisterHistory(services);
        services.AddAggregateRebuilder<PurchaseOrderAggregate, PurchaseOrderRebuilder>();
        return services.AddScoped<IPurchaseOrderRebuilding, PurchaseOrderRebuilding>();
    }

    public static IServiceCollection AddPurchaseOrderCommands(this IServiceCollection services)
    {
        RegisterStore(services);
        services.TryAddScoped(provider => new InlineStateReader<EventStream, PurchaseOrderStateRow>(
            provider.GetRequiredService<PurchasingDbContext>()
        ));
        return services.AddScoped<IPurchaseOrderCommands, PurchaseOrderCommands>();
    }

    public static IServiceCollection AddPurchaseOrderQueries(this IServiceCollection services)
    {
        services.TryAddScoped(provider => new InlineStateReader<EventStream, PurchaseOrderStateRow>(
            provider.GetRequiredService<PurchasingDbContext>()
        ));
        return services.AddScoped<IPurchaseOrderQueries, PurchaseOrderQueries>();
    }
}
