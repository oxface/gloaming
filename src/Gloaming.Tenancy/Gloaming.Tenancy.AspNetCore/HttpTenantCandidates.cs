using System.Net;
using Microsoft.AspNetCore.Http;

namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Optional request candidate extraction. Neither method resolves or admits a tenant.</summary>
public static class HttpTenantCandidates
{
    /// <summary>Reads one subdomain label beneath an explicit base domain, without admission.</summary>
    public static string? FromSubdomain(HttpContext context, string baseDomain)
    {
        ArgumentNullException.ThrowIfNull(context);
        string domain = ValidateBaseDomain(baseDomain);

        string host = WithoutTerminalDot(context.Request.Host.Host);
        if (IsDnsName(host) && !IPAddress.TryParse(host, out _))
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase))
                return null;
            string suffix = "." + domain;
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                string candidate = host[..^suffix.Length];
                if (IsDnsLabel(candidate))
                    return candidate;
            }
        }
        throw new HttpTenantResolutionException(
            "The host must be the configured base domain or one tenant label beneath it."
        );
    }

    internal static string ValidateBaseDomain(string baseDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDomain);
        string domain = WithoutTerminalDot(baseDomain);
        if (!IsDnsName(domain) || IPAddress.TryParse(domain, out _))
            throw new ArgumentException("Configure an ASCII DNS base domain.", nameof(baseDomain));
        return domain;
    }

    private static string WithoutTerminalDot(string value) =>
        value.EndsWith('.') ? value[..^1] : value;

    private static bool IsDnsName(string value) =>
        value.Length is > 0 and <= 253 && value.Split('.').All(IsDnsLabel);

    private static bool IsDnsLabel(string value) =>
        value.Length is > 0 and <= 63
        && char.IsAsciiLetterOrDigit(value[0])
        && char.IsAsciiLetterOrDigit(value[^1])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    /// <summary>Reads a named string route value, preserving accepted text exactly.</summary>
    public static string? FromRoute(HttpContext context, string routeValueName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeValueName);
        if (
            !context.Request.RouteValues.TryGetValue(routeValueName, out object? value)
            || value is null
        )
            return null;
        return value is string candidate && !string.IsNullOrWhiteSpace(candidate)
            ? candidate
            : throw new HttpTenantResolutionException(
                "The tenant route candidate must be a nonblank string."
            );
    }
}
