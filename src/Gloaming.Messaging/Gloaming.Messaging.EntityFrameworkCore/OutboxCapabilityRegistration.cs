using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Registers model-selected tracked-save validation for this capability.</summary>
public static class OutboxCapabilityRegistration
{
    /// <summary>Registers validation for one context; configuration and worker activation remain explicit.</summary>
    public static IServiceCollection AddOutboxValidation<TContext>(this IServiceCollection services)
        where TContext : DbContext =>
        services.TryAddGloamingCapability<TContext>(
            "outbox",
            CapabilityPhase.Validate,
            (_, context) => context.ValidateOutboxChanges()
        );
}
