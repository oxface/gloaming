using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.ActorIdentity.AspNetCore;

/// <summary>Explicit registration and pipeline setup for HTTP actor establishment.</summary>
public static class HttpActorContextExtensions
{
    /// <summary>
    /// Registers the scoped actor holder, resolver and wrapper around the native policy evaluator.
    /// Call once after registering native authentication and authorization.
    /// </summary>
    public static IServiceCollection AddHttpActorContext<TResolver>(
        this IServiceCollection services
    )
        where TResolver : class, IHttpActorContextResolver =>
        services.AddHttpActorContext<TResolver>(static provider => new PolicyEvaluator(
            provider.GetRequiredService<IAuthorizationService>()
        ));

    /// <summary>
    /// Registers HTTP actor establishment around an explicitly supplied inner evaluator.
    /// The factory must not resolve IPolicyEvaluator, which is the wrapper being registered.
    /// </summary>
    public static IServiceCollection AddHttpActorContext<TResolver>(
        this IServiceCollection services,
        Func<IServiceProvider, IPolicyEvaluator> innerEvaluatorFactory
    )
        where TResolver : class, IHttpActorContextResolver
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(innerEvaluatorFactory);

        services.AddScoped<ActorContextAccessor>();
        services.AddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IHttpActorContextResolver, TResolver>();
        services.AddTransient<IPolicyEvaluator>(provider => new ActorContextPolicyEvaluator(
            innerEvaluatorFactory(provider),
            provider.GetRequiredService<IHttpActorContextResolver>(),
            provider.GetRequiredService<IActorContextInitializer>()
        ));
        return services;
    }

    /// <summary>
    /// Completes actor establishment after UseAuthorization and before endpoints execute.
    /// Requires AddHttpActorContext or equivalent explicit service registration.
    /// </summary>
    public static IApplicationBuilder UseHttpActorContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ActorContextMiddleware>();
    }
}
