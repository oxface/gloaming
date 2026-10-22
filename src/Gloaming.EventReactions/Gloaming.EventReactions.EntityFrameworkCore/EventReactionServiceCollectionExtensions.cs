using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.EventReactions.EntityFrameworkCore;

/// <summary>Context-typed dependency registration and model selection for local reaction save composition.</summary>
public static class EventReactionServiceCollectionExtensions
{
    private const string CapabilityKey = "event-reactions";

    /// <summary>Marks this native model as requiring explicitly registered local-reaction save composition.</summary>
    public static ModelBuilder ConfigureEventReactions(this ModelBuilder model) =>
        model.RequireGloamingCapability(CapabilityKey);

    /// <summary>Registers scoped reactions and explicit sources for one typed context/module save integration.</summary>
    public static IServiceCollection AddEventReactions<TContext, TModule>(
        this IServiceCollection services,
        Action<EventReactionRegistration<TContext, TModule>> configure
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        if (services.Any(service => service.ServiceType == typeof(ContextRegistration<TContext>)))
            throw new InvalidOperationException(
                $"Register event reactions only once for {typeof(TContext).Name}; combine its reactions and sources in that registration."
            );

        var registration = new EventReactionRegistration<TContext, TModule>(services);
        configure(registration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(registration.MaxReactionInvocations);
        services.AddSingleton(new ContextRegistration<TContext>());
        services.AddScoped(registration.CreateDispatcher);
        services.AddScoped<IEventSink<TModule>>(provider => new ModuleSink<TModule>(
            provider.GetRequiredService<EventReactionDispatcher<TModule>>()
        ));
        return services.TryAddGloamingCapability<TContext>(
            CapabilityKey,
            CapabilityPhase.Prepare,
            (provider, context) =>
                provider
                    .GetRequiredService<EventReactionDispatcher<TModule>>()
                    .Dispatch(() => Sources(context, provider, registration)),
            (provider, context, token) =>
                provider
                    .GetRequiredService<EventReactionDispatcher<TModule>>()
                    .DispatchAsync(() => Sources(context, provider, registration), token)
        );
    }

    private static PendingEvents[] Sources<TContext, TModule>(
        TContext context,
        IServiceProvider provider,
        EventReactionRegistration<TContext, TModule> registration
    )
        where TContext : DbContext
    {
        var sources = registration.Collect(provider, context).ToArray();
        if (
            (
                provider.GetRequiredService<EventReactionDispatcher<TModule>>().PendingCount > 0
                || sources.Any(source => source.Count > 0)
            ) && context.Database.CurrentTransaction is null
        )
            throw new InvalidOperationException(
                "Begin the owning transaction before handling domain events."
            );

        return sources;
    }

    private sealed class ContextRegistration<TContext>
        where TContext : DbContext;

    private sealed class ModuleSink<TModule>(EventReactionDispatcher<TModule> dispatcher)
        : IEventSink<TModule>
    {
        public void Raise(object fact) => dispatcher.Sink.Raise(fact);
    }
}
