using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ModulithFoundry.Samples.Messaging.Rendering;

namespace ModulithFoundry.Samples.Messaging.Tests.Fixtures.Inbox;

// Finite demo credential policy. Real hosts select their own native authentication scheme.
internal sealed class RenderOperatorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    RenderOperatorCredential credential
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "RenderOperator";
    internal const string Permission = "rendering.inbox.retry";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (
            !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter)
        )
            return Task.FromResult(AuthenticateResult.NoResult());

        byte[] supplied = SHA256.HashData(Encoding.UTF8.GetBytes(header.Parameter));
        if (!CryptographicOperations.FixedTimeEquals(credential.Hash, supplied))
            return Task.FromResult(AuthenticateResult.Fail("Invalid operator credential."));

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "rendering-operator"),
                new Claim("permission", Permission),
            ],
            SchemeName
        );
        return Task.FromResult(
            AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)
            )
        );
    }
}

internal sealed class RenderOperatorCredential
{
    internal byte[] Hash { get; }

    internal RenderOperatorCredential(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
            throw new ArgumentException(
                "Configure an operator token of at least 32 characters.",
                nameof(token)
            );

        Hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
