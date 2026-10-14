using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gloaming.ActorIdentity.AspNetCore.Tests;

// Test-only scheme selection instrument. The executable sample has no header authentication.
internal sealed class SecondaryAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("X-Secondary"))
            return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("iss", "https://login.test"), new Claim("sub", "external-beta")],
                Scheme.Name
            )
        );
        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name))
        );
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers["X-Challenge-Scheme"] = Scheme.Name;
        return Task.CompletedTask;
    }
}
