using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Registers model-selected tracked-save validation for this capability.</summary>
public static class InboxCapabilityRegistration
{
    /// <summary>Registers validation for one context; configuration and worker activation remain explicit.</summary>
    public static IServiceCollection AddInboxValidation<TContext>(this IServiceCollection services)
        where TContext : DbContext =>
        services.TryAddGloamingCapability<TContext>(
            "inbox",
            CapabilityPhase.Validate,
            (_, context) => context.ValidateInboxChanges()
        );
}
