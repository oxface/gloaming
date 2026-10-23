using Gloaming.Samples.Wholesale.Access;
using Gloaming.Samples.Wholesale.Access.Contracts;

namespace Gloaming.Samples.Wholesale.Api;

// Only finite setup reads these inputs. Runtime authentication never creates external links.
internal static class DemoIdentitySetup
{
    public static DemoAccessIdentities? Read(IConfiguration configuration)
    {
        string? issuer = configuration["DemoIdentity:Issuer"];
        string? alpha = configuration["DemoIdentity:AlphaSubject"];
        string? beta = configuration["DemoIdentity:BetaSubject"];
        if (issuer is null && alpha is null && beta is null)
            return null;
        if (
            string.IsNullOrWhiteSpace(issuer)
            || !Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || issuer != issuer.Trim()
            || string.IsNullOrWhiteSpace(alpha)
            || string.IsNullOrWhiteSpace(beta)
            || alpha == beta
        )
            throw new InvalidOperationException(
                "Configure a HTTPS DemoIdentity:Issuer and distinct AlphaSubject/BetaSubject values together."
            );
        return new DemoAccessIdentities(
            new ExternalIdentity(issuer, alpha),
            new ExternalIdentity(issuer, beta)
        );
    }
}
