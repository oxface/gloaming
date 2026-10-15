using Gloaming.ActorIdentity;
using Gloaming.ActorIdentity.AspNetCore;
using Gloaming.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Access;
using ModulithFoundry.Samples.Wholesale.Access.Persistence;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo;

public static class DemoComposition
{
    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        NativeAuthentication.Configure(services, configuration);
        services.AddAuthorization(options =>
        {
            var authenticated = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.DefaultPolicy = authenticated;
            options.FallbackPolicy = authenticated;
        });
        string connection =
            configuration.GetConnectionString("Access")
            ?? throw new InvalidOperationException(
                "Configure ConnectionStrings:Access for the HTTP sample."
            );
        services.AddDbContext<AccessDbContext>(options =>
            AccessDatabase.Configure(options, connection)
        );
        services.AddAccessQueries();
        services.AddHttpActorContext<ApplicationActorResolver>();
        services.AddInventoryDatabase(connection);
        services.AddInventoryQueries();
        services.AddSalesDatabase(connection);
        services.AddCustomerProfiles();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "wholesale.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });
        services.AddSingleton<IAntiforgeryAdditionalDataProvider, ActorAntiforgeryData>();
        services
            .AddHealthChecks()
            .AddCheck<WholesaleDatabaseHealthCheck>(
                "wholesale-database",
                timeout: TimeSpan.FromSeconds(5)
            );
        services.AddProblemDetails();
        services.AddExceptionHandler<ContextExceptionHandler>();
    }

    public static void ConfigureHttp(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseHttpActorContext();
        app.UseHttpTenantContext();
        app.MapGet("/health", () => TypedResults.Ok("healthy")).AllowAnonymous().AllowTenantless();
        app.MapGet(
                "/antiforgery",
                (HttpContext context, IAntiforgery antiforgery) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    return TypedResults.Ok(
                        new AntiforgeryResponse(
                            antiforgery.GetAndStoreTokens(context).RequestToken!
                        )
                    );
                }
            )
            .RequireAuthorization()
            .AllowTenantless();
        app.MapGet("/identity", ReadIdentity).RequireAuthorization().AllowTenantless();
        app.MapGet("/public-identity", ReadIdentity).AllowAnonymous().AllowTenantless();
        app.MapGet(
                "/login",
                () =>
                    Results.Challenge(
                        new AuthenticationProperties { RedirectUri = "/identity" },
                        [NativeAuthentication.OidcScheme]
                    )
            )
            .AllowAnonymous()
            .AllowTenantless();
    }

    public static void MapOrganizationEndpoints(
        IEndpointRouteBuilder endpoints,
        string catalogPattern,
        string identityPattern,
        string stockPattern
    ) => OrganizationEndpoints.Map(endpoints, catalogPattern, identityPattern, stockPattern);

    private static Ok<IdentityResponse> ReadIdentity(IActorContextAccessor accessor)
    {
        Actor actor = accessor.Current.Actor;
        return TypedResults.Ok(new IdentityResponse(actor.Kind, actor.Id?.Value));
    }
}

public sealed record AntiforgeryResponse(string RequestToken);
