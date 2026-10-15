using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Messaging.Workflows.EntityFrameworkCore;

/// <summary>Registers model-selected tracked-save validation for this capability.</summary>
public static class WorkflowCapabilityRegistration
{
    /// <summary>Registers validation for one context; configuration and worker activation remain explicit.</summary>
    public static IServiceCollection AddWorkflowValidation<TContext>(
        this IServiceCollection services
    )
        where TContext : DbContext =>
        services.TryAddGloamingCapability<TContext>(
            "workflows",
            CapabilityPhase.Validate,
            (_, context) => context.ValidateWorkflowChanges()
        );
}
