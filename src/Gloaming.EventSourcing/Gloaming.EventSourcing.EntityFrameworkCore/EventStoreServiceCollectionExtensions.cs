using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Independent scoped writing and maintenance roles, using consumer-typed contexts.</summary>
public static class EventStoreServiceCollectionExtensions
{
    public static IServiceCollection AddEventStore<TAggregate, TStore>(
        this IServiceCollection services
    )
        where TAggregate : class
        where TStore : class, IEventStore<TAggregate>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<TStore>();
        services.TryAddScoped<IEventStore<TAggregate>>(provider =>
            provider.GetRequiredService<TStore>()
        );
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddAggregateRebuilder<TAggregate, TRebuilder>(
        this IServiceCollection services
    )
        where TAggregate : class
        where TRebuilder : class, IAggregateRebuilder<TAggregate>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<TRebuilder>();
        services.TryAddScoped<IAggregateRebuilder<TAggregate>>(provider =>
            provider.GetRequiredService<TRebuilder>()
        );
        return services;
    }

    public static IServiceCollection AddEventStore<TAggregate, TStore, TRebuilder>(
        this IServiceCollection services
    )
        where TAggregate : class
        where TStore : class, IEventStore<TAggregate>
        where TRebuilder : class, IAggregateRebuilder<TAggregate> =>
        services
            .AddEventStore<TAggregate, TStore>()
            .AddAggregateRebuilder<TAggregate, TRebuilder>();
}
