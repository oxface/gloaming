using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Module-typed, subscription-keyed consumer handler registration.</summary>
public static class InboxServiceCollectionExtensions
{
    /// <summary>Registers scoped consumer classification for one subscription; omission retains indefinite delayed retry.</summary>
    public static IServiceCollection AddInboxFailurePolicy<TDbContext, TPolicy>(
        this IServiceCollection services,
        string subscriptionKey
    )
        where TDbContext : DbContext
        where TPolicy : class, IInboxFailurePolicy<TDbContext>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);

        if (
            services.Any(item =>
                item.ServiceType == typeof(IInboxFailurePolicy<TDbContext>)
                && item.IsKeyedService
                && Equals(item.ServiceKey, subscriptionKey)
            )
        )
            throw new InvalidOperationException(
                "An inbox failure policy is already registered for this context/subscription."
            );

        return services.AddKeyedScoped<IInboxFailurePolicy<TDbContext>, TPolicy>(subscriptionKey);
    }

    /// <summary>Registers one scoped handler; conflicting registrations reject rather than select arbitrarily.</summary>
    /// <remarks>Register an owning context separately. The handler must establish admitted context before business queries.</remarks>
    public static IServiceCollection AddInboxHandler<TDbContext, THandler>(
        this IServiceCollection services,
        string subscriptionKey
    )
        where TDbContext : DbContext
        where THandler : class, IInboxHandler<TDbContext>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        if (
            services.Any(item =>
                item.ServiceType == typeof(IInboxHandler<TDbContext>)
                && item.IsKeyedService
                && Equals(item.ServiceKey, subscriptionKey)
            )
        )
            throw new InvalidOperationException(
                "An inbox handler is already registered for this context/subscription."
            );
        return services.AddKeyedScoped<IInboxHandler<TDbContext>, THandler>(subscriptionKey);
    }
}
