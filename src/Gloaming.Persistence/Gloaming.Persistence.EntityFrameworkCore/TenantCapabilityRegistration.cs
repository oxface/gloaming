using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Persistence.EntityFrameworkCore;

/// <summary>Registers model-selected tracked-save validation for this capability.</summary>
public static class TenantCapabilityRegistration
{
    /// <summary>Registers validation for one context; configuration and worker activation remain explicit.</summary>
    public static IServiceCollection AddTenantOwnershipValidation<TContext, TKey>(
        this IServiceCollection services,
        Func<TContext, TKey> requireTenant
    )
        where TContext : DbContext
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(requireTenant);

        return services.TryAddGloamingCapability<TContext>(
            "tenant-ownership",
            CapabilityPhase.Validate,
            (_, context) => context.ValidateTenantChanges(() => requireTenant(context))
        );
    }
}
