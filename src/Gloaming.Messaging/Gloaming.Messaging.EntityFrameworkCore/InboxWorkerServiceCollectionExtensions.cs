using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Opt-in sequential hosting for a module/subscription's registered processor.</summary>
public static class InboxWorkerServiceCollectionExtensions
{
    /// <summary>Runs each attempt in a fresh scope; does not create schemas or establish trusted tenants.</summary>
    public static IServiceCollection AddInboxWorker<TDbContext>(
        this IServiceCollection services,
        string subscriptionKey,
        InboxWorkerOptions options
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        ArgumentNullException.ThrowIfNull(options);
        if (
            options.IdleDelay <= TimeSpan.Zero
            || options.IdleDelay.TotalMilliseconds > int.MaxValue
            || options.FailureDelay <= TimeSpan.Zero
            || options.FailureDelay.TotalMilliseconds > int.MaxValue
        )
            throw new ArgumentOutOfRangeException(nameof(options));
        services.AddSingleton<IHostedService>(provider => new InboxWorker<TDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<InboxWorker<TDbContext>>>(),
            subscriptionKey,
            options
        ));
        return services;
    }
}
