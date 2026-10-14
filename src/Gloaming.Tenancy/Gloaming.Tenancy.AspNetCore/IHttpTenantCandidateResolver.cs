using Microsoft.AspNetCore.Http;

namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Consumer-owned canonical lookup and admission for a selected, untrusted candidate.</summary>
public interface IHttpTenantCandidateResolver
{
    /// <summary>
    /// A null candidate means absence, not failure. Return deliberate tenantless context only
    /// when appropriate; null result means failed resolution/admission. Do not change identity or routing.
    /// </summary>
    ValueTask<TenantContext?> ResolveAsync(
        string? candidate,
        HttpContext httpContext,
        CancellationToken cancellationToken
    );
}
