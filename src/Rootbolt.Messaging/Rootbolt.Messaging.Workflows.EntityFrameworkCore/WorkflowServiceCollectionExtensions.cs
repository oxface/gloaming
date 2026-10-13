using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rootbolt.Messaging.EntityFrameworkCore;
using Rootbolt.Messaging.Workflows;

namespace Rootbolt.Messaging.Workflows.EntityFrameworkCore;

/// <summary>Typed, module-specific dependency registration without a shared untyped DbContext.</summary>
public static class WorkflowServiceCollectionExtensions
{
    /// <summary>Register the scoped definition and runner. The owning typed outbox and context remain consumer registrations.</summary>
    public static IServiceCollection AddWorkflow<TDbContext, TStart, TState>(
        this IServiceCollection services,
        Func<IServiceProvider, WorkflowDefinition<TStart, TState>> definitionFactory
    )
        where TDbContext : DbContext
        where TStart : class
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitionFactory);

        services.AddScoped<IWorkflow<TStart, TState>>(provider => new EfWorkflow<
            TDbContext,
            TStart,
            TState
        >(
            provider.GetRequiredService<TDbContext>(),
            provider.GetRequiredService<IOutbox<TDbContext>>(),
            definitionFactory(provider)
        ));
        return services;
    }
}
