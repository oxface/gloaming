using Microsoft.AspNetCore.Http;

namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Consumer-owned canonical tenant resolution and admission before publication.</summary>
public interface IHttpTenantContextResolver
{
    /// <summary>
    /// Returns an admitted tenant or deliberate tenantless context. Null means resolution failed,
    /// even when tenantless execution is permitted. Do not change request identity or routing.
    /// </summary>
    ValueTask<TenantContext?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    );
}
