using Microsoft.AspNetCore.HostFiltering;

namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Opt-in configuration for native host filtering. This performs no tenant admission.</summary>
public static class HttpTenantHostFilteringExtensions
{
    /// <summary>
    /// Replaces the allowed-host list with the validated base domain and its subdomains,
    /// including terminal-dot forms. Other native options and proxy configuration remain consumer-owned.
    /// </summary>
    public static void UseTenantSubdomainHosts(this HostFilteringOptions options, string baseDomain)
    {
        ArgumentNullException.ThrowIfNull(options);
        string domain = HttpTenantCandidates.ValidateBaseDomain(baseDomain);
        options.AllowedHosts = [domain, "*." + domain, domain + ".", "*." + domain + "."];
    }
}
