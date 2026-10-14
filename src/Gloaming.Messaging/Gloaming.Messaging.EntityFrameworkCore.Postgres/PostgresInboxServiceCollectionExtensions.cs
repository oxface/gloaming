using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Independent intake and local processing registration for an explicit module context.</summary>
public static class PostgresInboxServiceCollectionExtensions
{
    /// <summary>Registers scoped retained intake. Requires no handler, outbox, publisher or worker.</summary>
    public static IServiceCollection AddPostgresInbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IInbox<TDbContext>, PostgresInbox<TDbContext>>();
        return services;
    }

    /// <summary>Registers scoped processing; register subscription-keyed handlers independently.</summary>
    /// <remarks>Owns one ReadCommitted transaction per attempt. Does not start a worker or create tables.</remarks>
    public static IServiceCollection AddPostgresInboxProcessor<TDbContext>(
        this IServiceCollection services,
        InboxProcessingOptions options
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (options.RetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Use a positive inbox retry delay."
            );
        services.TryAddScoped<IInboxProcessor<TDbContext>>(
            provider => new PostgresInboxProcessor<TDbContext>(
                provider.GetRequiredService<TDbContext>(),
                provider,
                options,
                provider.GetService<ILogger<PostgresInboxProcessor<TDbContext>>>()
            )
        );
        return services;
    }
}
