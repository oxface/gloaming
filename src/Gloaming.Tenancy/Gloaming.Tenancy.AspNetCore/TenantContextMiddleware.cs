using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Establishes the admitted tenant before endpoint work, after native authorization.</summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TenantRequirement _defaultRequirement;

    /// <summary>Binds the next middleware and default endpoint tenancy requirement.</summary>
    public TenantContextMiddleware(RequestDelegate next, IOptions<HttpTenantContextOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentNullException.ThrowIfNull(options);

        _defaultRequirement = options.Value.DefaultRequirement;
        if (!Enum.IsDefined(_defaultRequirement))
            throw new ArgumentException("Choose a defined tenant requirement.", nameof(options));
    }

    /// <summary>Resolves consumer admission, establishes immutable tenancy once and invokes the endpoint only when its tenancy requirement is satisfied.</summary>
    public async Task InvokeAsync(
        HttpContext context,
        IHttpTenantContextResolver resolver,
        ITenantContextInitializer initializer
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(initializer);

        Endpoint? endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await _next(context);
            return;
        }

        CancellationToken cancellationToken = context.RequestAborted;
        cancellationToken.ThrowIfCancellationRequested();

        ClaimsPrincipal principal = context.User;
        TenantRequirement requirement =
            endpoint.Metadata.GetMetadata<TenantRequirementAttribute>()?.Requirement
            ?? _defaultRequirement;
        TenantContext? resolved = await resolver.ResolveAsync(context, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Resolution may await; admission must still concern the same request principal.
        if (!ReferenceEquals(principal, context.User))
            throw new HttpTenantResolutionException(
                "Tenant resolution must not replace the request principal."
            );

        TenantContext tenant =
            resolved
            ?? throw new HttpTenantResolutionException(
                "The request could not be resolved to an admitted tenant context."
            );
        if (requirement == TenantRequirement.Required)
            tenant.RequireTenant();

        initializer.Initialize(tenant);
        await _next(context);
    }
}
