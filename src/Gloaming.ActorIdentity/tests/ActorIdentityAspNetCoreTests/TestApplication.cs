using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gloaming.ActorIdentity.AspNetCore.Tests;

internal static class TestApplication
{
    internal static async Task<WebApplication> StartAsync(
        RequestObservations observations,
        bool fallbackPolicy = true,
        bool replacePrincipal = false
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder
            .Services.AddAuthentication("cookie")
            .AddCookie("cookie")
            .AddScheme<AuthenticationSchemeOptions, SecondaryAuthenticationHandler>(
                "secondary",
                _ => { }
            );
        builder.Services.AddAuthorization(options =>
        {
            if (fallbackPolicy)
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            options.AddPolicy(
                "ObserveActor",
                policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new ObserveActorRequirement())
            );
            options.AddPolicy(
                "Selected",
                policy =>
                    policy
                        .AddAuthenticationSchemes("secondary")
                        .RequireAuthenticatedUser()
                        .AddRequirements(new ObserveActorRequirement())
            );
            options.AddPolicy(
                "Permissive",
                policy =>
                    policy
                        .RequireAssertion(_ => true)
                        .AddRequirements(new ObserveActorRequirement())
            );
            options.AddPolicy(
                "DeniedPermission",
                policy =>
                    policy
                        .RequireAuthenticatedUser()
                        .RequireClaim("permission", "change")
                        .AddRequirements(new ObserveActorRequirement())
            );
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(SelectedController).Assembly);
        builder.Services.AddSingleton(observations);
        builder.Services.AddScoped<IAuthorizationHandler, ObserveActorHandler>();
        builder.Services.AddHttpActorContext<FixtureResolver>(
            provider => new ObservingPolicyEvaluator(
                provider.GetRequiredService<IAuthorizationService>(),
                observations
            )
        );
        var app = builder.Build();
        app.Use(
            async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (HttpActorResolutionException)
                {
                    context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                    await context.Response.WriteAsync(
                        "consumer mapping rejection",
                        CancellationToken.None
                    );
                }
                catch (OperationCanceledException)
                    when (context.RequestAborted.IsCancellationRequested)
                {
                    bool published;
                    try
                    {
                        _ = context
                            .RequestServices.GetRequiredService<IActorContextAccessor>()
                            .Current;
                        published = true;
                    }
                    catch (InvalidOperationException)
                    {
                        published = false;
                    }
                    observations.CancellationObserved.TrySetResult(published);
                    throw;
                }
            }
        );
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        if (replacePrincipal)
            app.Use(
                async (context, next) =>
                {
                    observations.BeforeReplacement = Snapshot(
                        context.RequestServices.GetRequiredService<IActorContextAccessor>()
                    );
                    context.User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim("sub", "external-beta")], "replacement")
                    );
                    await next(context);
                }
            );
        app.UseHttpActorContext();
        app.MapGet("/protected", Read).RequireAuthorization();
        app.MapGet("/fallback", Read);
        app.MapGet("/public", Read).AllowAnonymous();
        app.MapGet("/permissive", Read).RequireAuthorization("Permissive");
        app.MapGet("/selected", Read).RequireAuthorization("Selected");
        app.MapGet("/forbidden", Read).RequireAuthorization("DeniedPermission");
        app.MapGet("/cancel", Read).RequireAuthorization();
        app.MapGet(
                "/overlap",
                async (HttpContext context, IActorContextAccessor accessor) =>
                {
                    if (Interlocked.Increment(ref observations.OverlappingRequests) == 2)
                        observations.BothRequestsEntered.TrySetResult();
                    await observations.BothRequestsEntered.Task.WaitAsync(
                        TimeSpan.FromSeconds(10),
                        context.RequestAborted
                    );
                    return Read(context, accessor);
                }
            )
            .RequireAuthorization();
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;

        IResult Read(HttpContext context, IActorContextAccessor accessor)
        {
            observations.EndpointCalls.AddOrUpdate(
                context.Request.Path.Value!,
                1,
                (_, count) => count + 1
            );
            return Results.Text(Snapshot(accessor));
        }
    }

    internal static HttpRequestMessage Request(WebApplication app, string path, string subject)
    {
        var options = app
            .Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get("cookie");
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("iss", "https://login.test"), new Claim("sub", subject)],
                "cookie"
            )
        );
        string ticket = options.TicketDataFormat.Protect(
            new AuthenticationTicket(principal, "cookie")
        );
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", options.Cookie.Name + "=" + ticket);
        return request;
    }

    internal static string Snapshot(IActorContextAccessor accessor)
    {
        ActorContext current = accessor.Current;
        return $"{current.Actor.Kind}:{current.Actor.Id?.Value ?? "none"}:{current.Initiator?.Id?.Value ?? "none"}";
    }
}

internal sealed class RequestObservations
{
    internal ConcurrentDictionary<string, int> MappingCalls { get; } = new();
    internal ConcurrentDictionary<string, int> EndpointCalls { get; } = new();
    internal ConcurrentQueue<(string? Subject, string Actor)> AuthorizationActors { get; } = new();
    internal TaskCompletionSource ResolverEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> CancellationObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource BothRequestsEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int OverlappingRequests;
    internal int NativeAuthentications;
    internal int NativeAuthorizations;
    internal string? BeforeReplacement { get; set; }
}

internal sealed class FixtureResolver(RequestObservations observations) : IHttpActorContextResolver
{
    public async ValueTask<ActorContext?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        observations.MappingCalls.AddOrUpdate(
            httpContext.Request.Path.Value!,
            1,
            (_, count) => count + 1
        );
        if (httpContext.User.FindFirst("sub")?.Value == "deferred")
        {
            observations.ResolverEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        return httpContext.User.FindFirst("sub")?.Value switch
        {
            "external-alpha" => new ActorContext(Actor.Human(new ActorId("application-alpha"))),
            "external-beta" => new ActorContext(Actor.Human(new ActorId("application-beta"))),
            "workflow" => new ActorContext(
                Actor.System(new ActorId("maintenance")),
                Actor.Human(new ActorId("initiating-user"))
            ),
            "anonymous-output" => new ActorContext(Actor.Anonymous),
            _ => null,
        };
    }
}

internal sealed class ObserveActorRequirement : IAuthorizationRequirement;

internal sealed class ObserveActorHandler(
    IActorContextAccessor accessor,
    RequestObservations observations
) : AuthorizationHandler<ObserveActorRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ObserveActorRequirement requirement
    )
    {
        observations.AuthorizationActors.Enqueue(
            (context.User.FindFirst("sub")?.Value, TestApplication.Snapshot(accessor))
        );
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

internal sealed class ObservingPolicyEvaluator(
    IAuthorizationService authorization,
    RequestObservations observations
) : PolicyEvaluator(authorization)
{
    public override async Task<AuthenticateResult> AuthenticateAsync(
        AuthorizationPolicy policy,
        HttpContext context
    )
    {
        Interlocked.Increment(ref observations.NativeAuthentications);
        return await base.AuthenticateAsync(policy, context);
    }

    public override Task<PolicyAuthorizationResult> AuthorizeAsync(
        AuthorizationPolicy policy,
        AuthenticateResult authenticationResult,
        HttpContext context,
        object? resource
    )
    {
        Interlocked.Increment(ref observations.NativeAuthorizations);
        return base.AuthorizeAsync(policy, authenticationResult, context, resource);
    }
}
