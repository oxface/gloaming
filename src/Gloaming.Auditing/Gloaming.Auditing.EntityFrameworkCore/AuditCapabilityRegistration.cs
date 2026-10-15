using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Auditing.EntityFrameworkCore;

/// <summary>Registers model-selected tracked-save validation for this capability.</summary>
public static class AuditCapabilityRegistration
{
    /// <summary>Registers validation for one context; configuration and worker activation remain explicit.</summary>
    public static IServiceCollection AddAuditValidation<TContext>(this IServiceCollection services)
        where TContext : DbContext =>
        services.TryAddGloamingCapability<TContext>(
            AuditModelMetadata.CapabilityKey,
            CapabilityPhase.Validate,
            (_, context) => context.ValidateAuditChanges()
        );
}
