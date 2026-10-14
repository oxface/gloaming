using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Inventory.Messaging;
using ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

namespace ModulithFoundry.Samples.Wholesale.Inventory;

public static class InventoryRegistration
{
    /// <summary>Registers reservation intake/processing; compose with stock commands and consumer transport.</summary>
    public static IServiceCollection AddStockReservationInbox(this IServiceCollection services)
    {
        services.TryAddScoped<ActorContextAccessor>();
        services.TryAddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.TryAddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddPostgresInbox<InventoryDbContext>();
        services.AddPostgresInboxProcessor<InventoryDbContext>(new(TimeSpan.FromSeconds(1)));
        return services.AddInboxHandler<InventoryDbContext, Reservations.StockReservationHandler>(
            StockReservationAdmission.Subscription
        );
    }

    public static IServiceCollection AddInventoryQueries(this IServiceCollection services) =>
        services.AddScoped<IStockCatalog, StockCatalog>();

    public static IServiceCollection AddStockPositionHistory(this IServiceCollection services) =>
        RegisterHistory(services);

    private static IServiceCollection RegisterHistory(IServiceCollection services)
    {
        services.TryAddScoped<StockPositionHistoryReader>();
        services.TryAddScoped<IStockPositionHistory>(provider =>
            provider.GetRequiredService<StockPositionHistoryReader>()
        );
        return services;
    }

    private static void RegisterStore(IServiceCollection services)
    {
        services.AddEventStore<StockPositionAggregate, StockPositionStore>();
    }

    public static IServiceCollection AddStockPositionRebuilding(this IServiceCollection services)
    {
        RegisterHistory(services);
        services.AddAggregateRebuilder<StockPositionAggregate, StockPositionRebuilder>();
        return services.AddScoped<IStockPositionRebuilding, StockPositionRebuilding>();
    }

    public static IServiceCollection AddStockPositionCommands(this IServiceCollection services)
    {
        services.AddPostgresOutbox<InventoryDbContext>();
        services.TryAddScoped<InventoryMessageContext>();
        services.TryAddScoped<StockIssueMessages>();
        RegisterStore(services);
        services.TryAddScoped(provider => new InlineStateReader<EventStream, StockPositionStateRow>(
            provider.GetRequiredService<InventoryDbContext>()
        ));
        return services.AddScoped<IStockPositionCommands, StockPositionCommands>();
    }

    public static IServiceCollection AddStockIssueInbox(this IServiceCollection services)
    {
        services.TryAddScoped<ActorContextAccessor>();
        services.TryAddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.TryAddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IAudit<InventoryDbContext>, EfAudit<InventoryDbContext>>();
        services.AddScoped<InventoryAudit>();
        services.AddPostgresInbox<InventoryDbContext>();
        services.AddPostgresInboxProcessor<InventoryDbContext>(new(TimeSpan.FromSeconds(1)));
        return services.AddInboxHandler<InventoryDbContext, StockIssueInboxHandler>(
            StockIssueMessageAdmission.Subscription
        );
    }

    public static IServiceCollection AddStockPositionQueries(this IServiceCollection services)
    {
        services.TryAddScoped(provider => new InlineStateReader<EventStream, StockPositionStateRow>(
            provider.GetRequiredService<InventoryDbContext>()
        ));
        return services.AddScoped<IStockPositionQueries, StockPositionQueries>();
    }
}
