using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Registers model-selected tracked-save validation for this capability.</summary>
public static class EventStreamCapabilityRegistration
{
    /// <summary>Registers validation for one context; configuration and worker activation remain explicit.</summary>
    public static IServiceCollection AddEventStreamValidation<TContext>(
        this IServiceCollection services
    )
        where TContext : DbContext =>
        services.TryAddGloamingCapability<TContext>(
            "event-streams",
            CapabilityPhase.Validate,
            (_, context) => context.ValidateEventStreamChanges(),
            (_, context, cancellationToken) =>
                RequiredInlineStateExtensions.ValidateEventStreamChangesAsync(
                    context,
                    cancellationToken
                )
        );
}
