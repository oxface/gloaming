using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Explicit consumer failure policy binding for one owning context.</summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>Registers one scoped policy; omission retains indefinite delayed retry with durable observations.</summary>
    public static IServiceCollection AddOutboxFailurePolicy<TDbContext, TPolicy>(
        this IServiceCollection services
    )
        where TDbContext : DbContext
        where TPolicy : class, IOutboxFailurePolicy<TDbContext>
    {
        ArgumentNullException.ThrowIfNull(services);
        if (
            services.Any(item =>
                item.ServiceType == typeof(IOutboxFailurePolicy<TDbContext>) && !item.IsKeyedService
            )
        )
            throw new InvalidOperationException(
                "An outbox failure policy is already registered for this context."
            );

        return services.AddScoped<IOutboxFailurePolicy<TDbContext>, TPolicy>();
    }
}
