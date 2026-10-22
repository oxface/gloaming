using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gloaming.Persistence.EntityFrameworkCore.Postgres;

/// <summary>Registers scoped native PostgreSQL tenant transaction setup without choosing admission or deploying policies.</summary>
public static class PostgresTenantRegistration
{
    /// <summary>Registers automatic tenant setup for caller-owned native transactions.</summary>
    public static IServiceCollection AddPostgresTenantRowSecurity<TContext>(
        this IServiceCollection services
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<PostgresTenantInterceptor<TContext>>();
        return services.TryAddGloamingCapability<TContext>(
            TenantRowSecurityExtensions.Capability,
            CapabilityPhase.Validate,
            (provider, context) =>
                provider
                    .GetRequiredService<PostgresTenantInterceptor<TContext>>()
                    .ValidateSave(context)
        );
    }

    /// <summary>Installs the native transaction/command interceptor and existing save capabilities.
    /// Use scoped, unpooled DbContext options with the operation's provider.</summary>
    public static DbContextOptionsBuilder UseRls<TContext>(
        this DbContextOptionsBuilder options,
        IServiceProvider provider
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);

        options.ConfigureGloamingCapabilities<TContext>(provider);
        if (
            options
                .Options.FindExtension<CoreOptionsExtension>()
                ?.Interceptors?.Any(interceptor =>
                    interceptor is PostgresTenantInterceptor<TContext>
                ) != true
        )
            options.AddInterceptors(
                provider.GetRequiredService<PostgresTenantInterceptor<TContext>>()
            );

        return options;
    }
}
