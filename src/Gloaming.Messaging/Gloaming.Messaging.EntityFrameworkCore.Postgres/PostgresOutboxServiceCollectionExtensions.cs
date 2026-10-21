using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Independent producer/dispatcher registration bound to an explicit module context.</summary>
public static class PostgresOutboxServiceCollectionExtensions
{
    /// <summary>Registers bounded metadata inspection and conditional retry; authorize each invocation in consumer code.</summary>
    public static IServiceCollection AddPostgresOutboxDeadLetters<TDbContext>(
        this IServiceCollection services
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<
            IOutboxDeadLetters<TDbContext>,
            PostgresOutboxDeadLetters<TDbContext>
        >();
        return services;
    }

    /// <summary>Registers scoped enqueue support and validates the native model when the service is resolved.</summary>
    /// <typeparam name="TDbContext">The separately registered owning Npgsql context.</typeparam>
    public static IServiceCollection AddPostgresOutbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IOutbox<TDbContext>>(provider =>
        {
            var database = provider.GetRequiredService<TDbContext>();
            PostgresOutboxDispatcher<TDbContext>.ValidateModel(database);
            return new EfOutbox<TDbContext>(database);
        });
        return services;
    }

    /// <summary>Registers a scoped dispatcher and publisher binding for this module only.</summary>
    /// <remarks>Does not register a global IMessagePublisher or start a worker. Existing typed registrations are retained.</remarks>
    /// <typeparam name="TDbContext">The separately registered owning Npgsql context.</typeparam>
    /// <typeparam name="TPublisher">Consumer publisher interpreting routing keys and defining transport acceptance.</typeparam>
    public static IServiceCollection AddPostgresOutboxDispatcher<TDbContext, TPublisher>(
        this IServiceCollection services,
        OutboxDispatchOptions options
    )
        where TDbContext : DbContext
        where TPublisher : class, IMessagePublisher
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        PostgresOutboxDispatcher<TDbContext>.ValidateOptions(options);
        services.TryAddScoped<TPublisher>();
        services.TryAddScoped<IOutboxDispatcher<TDbContext>>(
            provider => new PostgresOutboxDispatcher<TDbContext>(
                provider.GetRequiredService<TDbContext>(),
                provider.GetRequiredService<TPublisher>(),
                options,
                provider.GetService<ILogger<PostgresOutboxDispatcher<TDbContext>>>(),
                provider.GetService<IOutboxFailurePolicy<TDbContext>>()
            )
        );
        return services;
    }
}
