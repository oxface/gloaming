using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.EntityFrameworkCore.Hosting;

/// <summary>Opt-in hosting for an already registered module-specific dispatcher.</summary>
public static class OutboxWorkerServiceCollectionExtensions
{
    /// <summary>Registers a sequential worker that resolves this module's dispatcher in a fresh scope per attempt.</summary>
    /// <remarks>Does not register the context, dispatcher or transport, create tables or save producer changes.</remarks>
    /// <typeparam name="TDbContext">The typed context whose dispatcher the worker invokes.</typeparam>
    public static IServiceCollection AddOutboxWorker<TDbContext>(
        this IServiceCollection services,
        OutboxWorkerOptions options
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (
            options.IdleDelay <= TimeSpan.Zero
            || options.IdleDelay.TotalMilliseconds > int.MaxValue
            || options.FailureDelay <= TimeSpan.Zero
            || options.FailureDelay.TotalMilliseconds > int.MaxValue
        )
            throw new ArgumentOutOfRangeException(nameof(options));

        services.AddSingleton<IHostedService>(provider => new OutboxWorker<TDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<OutboxWorker<TDbContext>>>(),
            options
        ));

        return services;
    }
}
