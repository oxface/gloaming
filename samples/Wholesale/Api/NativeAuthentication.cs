using Microsoft.AspNetCore.Authentication.Cookies;

namespace ModulithFoundry.Samples.Wholesale.Api;

// Editable consumer configuration, not a runtime-library authentication facade.
internal static class NativeAuthentication
{
    internal const string OidcScheme = "oidc";

    internal static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        string authority =
            configuration["Oidc:Authority"]
            ?? throw new InvalidOperationException(
                "Configure Oidc:Authority for the HTTP identity demo."
            );
        string clientId =
            configuration["Oidc:ClientId"]
            ?? throw new InvalidOperationException(
                "Configure Oidc:ClientId for the HTTP identity demo."
            );
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "wholesale.identity";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.LoginPath = "/login";
            })
            .AddOpenIdConnect(
                OidcScheme,
                options =>
                {
                    options.Authority = authority;
                    options.ClientId = clientId;
                    options.ClientSecret = configuration["Oidc:ClientSecret"];
                    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.ResponseType = "code";
                    options.UsePkce = true;
                    options.MapInboundClaims = false;
                    // Keep the validated issuer; native defaults otherwise delete this claim.
                    options.ClaimActions.Remove("iss");
                    options.CallbackPath = "/signin-oidc";
                    options.SaveTokens = false;
                    options.TokenValidationParameters.NameClaimType = "sub";
                }
            );
    }
}
