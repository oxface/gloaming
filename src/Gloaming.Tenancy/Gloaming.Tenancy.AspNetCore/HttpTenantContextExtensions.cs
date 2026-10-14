using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Explicit service and pipeline setup for HTTP tenant establishment.</summary>
public static class HttpTenantContextExtensions
{
    /// <summary>Registers one scoped holder and resolver, plus validated native options. Call once.</summary>
    public static IServiceCollection AddHttpTenantContext<TResolver>(
        this IServiceCollection services,
        Action<HttpTenantContextOptions>? configure = null
    )
        where TResolver : class, IHttpTenantContextResolver
    {
        ArgumentNullException.ThrowIfNull(services);
        AddContextServices(services, configure);
        services.AddScoped<IHttpTenantContextResolver, TResolver>();
        return services;
    }

    /// <summary>Registers named route selection and a scoped consumer lookup/admission resolver. Call once.</summary>
    public static IServiceCollection AddRouteTenancy<TResolver>(
        this IServiceCollection services,
        string routeValueName,
        Action<HttpTenantContextOptions>? configure = null
    )
        where TResolver : class, IHttpTenantCandidateResolver
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeValueName);
        return AddCandidateResolution<TResolver>(
            services,
            context => HttpTenantCandidates.FromRoute(context, routeValueName),
            configure
        );
    }

    /// <summary>Registers one-label subdomain selection and consumer lookup/admission. Host filtering is separately configured.</summary>
    public static IServiceCollection AddSubdomainTenancy<TResolver>(
        this IServiceCollection services,
        string baseDomain,
        Action<HttpTenantContextOptions>? configure = null
    )
        where TResolver : class, IHttpTenantCandidateResolver
    {
        ArgumentNullException.ThrowIfNull(services);
        string domain = HttpTenantCandidates.ValidateBaseDomain(baseDomain);
        return AddCandidateResolution<TResolver>(
            services,
            context => HttpTenantCandidates.FromSubdomain(context, domain),
            configure
        );
    }

    private static IServiceCollection AddCandidateResolution<TResolver>(
        IServiceCollection services,
        Func<HttpContext, string?> select,
        Action<HttpTenantContextOptions>? configure
    )
        where TResolver : class, IHttpTenantCandidateResolver
    {
        AddContextServices(services, configure);
        services.AddScoped<TResolver>();
        services.AddScoped<IHttpTenantContextResolver>(
            provider => new CandidateContextResolver<TResolver>(
                provider.GetRequiredService<TResolver>(),
                select
            )
        );
        return services;
    }

    private static void AddContextServices(
        IServiceCollection services,
        Action<HttpTenantContextOptions>? configure
    )
    {
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        var options = services.AddOptions<HttpTenantContextOptions>();
        if (configure is not null)
            options.Configure(configure);
        options
            .Validate(
                value => Enum.IsDefined(value.DefaultRequirement),
                "Choose a defined tenant requirement."
            )
            .ValidateOnStart();
    }

    private sealed class CandidateContextResolver<TResolver>(
        TResolver resolver,
        Func<HttpContext, string?> select
    ) : IHttpTenantContextResolver
        where TResolver : class, IHttpTenantCandidateResolver
    {
        public ValueTask<TenantContext?> ResolveAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken
        ) => resolver.ResolveAsync(select(httpContext), httpContext, cancellationToken);
    }

    /// <summary>Adds tenant establishment after native authorization and any actor completion.</summary>
    public static IApplicationBuilder UseHttpTenantContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<TenantContextMiddleware>();
    }

    /// <summary>Requires a selected tenant independently of native authorization policies.</summary>
    public static TBuilder RequireTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new TenantRequirementAttribute(TenantRequirement.Required));

    /// <summary>Permits explicit tenantless execution while retaining a successfully selected tenant.</summary>
    public static TBuilder AllowTenantless<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new TenantRequirementAttribute(TenantRequirement.TenantlessAllowed));
}
