using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ModulithFoundry.Samples.OutboxDemo;

// Finite demo credential policy. Real hosts select their own native authentication scheme.
internal sealed class ExportOperatorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ExportOperatorCredential credential
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "ExportOperator";
    internal const string Permission = "exports.outbox.retry";

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
                new Claim(ClaimTypes.NameIdentifier, "exports-operator"),
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

internal sealed class ExportOperatorCredential
{
    internal byte[] Hash { get; }

    internal ExportOperatorCredential(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
            throw new ArgumentException(
                "Configure an operator token of at least 32 characters.",
                nameof(token)
            );

        Hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
