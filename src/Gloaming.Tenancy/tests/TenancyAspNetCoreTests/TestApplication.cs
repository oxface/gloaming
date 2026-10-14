using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gloaming.Tenancy.AspNetCore.Tests;

internal static class TestApplication
{
    internal static async Task<WebApplication> StartAsync(
        TenantRequirement defaultRequirement = TenantRequirement.Required,
        RequestObservations? observations = null,
        bool fallbackPolicy = false
    )
    {
        observations ??= new RequestObservations();
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder
            .Services.AddAuthentication("primary")
            .AddCookie("primary", options => options.Cookie.Name = "primary-cookie")
            .AddCookie("selected", options => options.Cookie.Name = "selected-cookie");
        builder.Services.AddAuthorization(options =>
        {
            if (fallbackPolicy)
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            options.AddPolicy("Permissive", policy => policy.RequireAssertion(_ => true));
            options.AddPolicy(
                "Change",
                policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "change")
            );
            options.AddPolicy(
                "Selected",
                policy => policy.AddAuthenticationSchemes("selected").RequireAuthenticatedUser()
            );
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(TenantController).Assembly);
        builder.Services.AddSingleton(observations);
        if (observations.FromUser)
            builder.Services.AddHttpTenantContext<FixtureResolver>(options =>
                options.DefaultRequirement = defaultRequirement
            );
        else if (observations.BaseDomain is { } domain)
            builder.Services.AddSubdomainTenancy<FixtureResolver>(
                domain,
                options => options.DefaultRequirement = defaultRequirement
            );
        else
            builder.Services.AddRouteTenancy<FixtureResolver>(
                "workspace",
                options => options.DefaultRequirement = defaultRequirement
            );
        var app = builder.Build();
        app.Use(
            async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (TenantRequiredException)
                {
                    observations.FailurePublished = IsPublished(context);
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
                catch (HttpTenantResolutionException)
                {
                    observations.FailurePublished = IsPublished(context);
                    context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                }
                catch (OperationCanceledException)
                    when (context.RequestAborted.IsCancellationRequested)
                {
                    observations.Cancelled.TrySetResult(IsPublished(context));
                    throw;
                }
            }
        );
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(
            (context, next) =>
            {
                observations.BeforeTenant?.Invoke(context);
                return next(context);
            }
        );
        app.UseHttpTenantContext();
        app.MapGet("/scope/{workspace?}", Read);
        app.MapGet("/public/{workspace?}", Read).AllowAnonymous();
        app.MapGet("/permissive/{workspace?}", Read).RequireAuthorization("Permissive");
        app.MapGet("/permission/{workspace?}", Read).RequireAuthorization("Change");
        app.MapGet("/selected/{workspace?}", Read).RequireAuthorization("Selected");
        app.MapGet("/optional/{workspace?}", Read).AllowAnonymous().AllowTenantless();
        var required = app.MapGroup("/required").RequireTenant();
        var allowed = required.MapGroup("/allowed").AllowTenantless();
        allowed.MapGet("/read", Read);
        allowed.MapGet("/strict", Read).RequireTenant();
        app.MapGroup("/allowed")
            .AllowTenantless()
            .MapGroup("/required")
            .RequireTenant()
            .MapGet("/read", Read);
        app.MapGet("/last", Read).AllowTenantless().RequireTenant().AllowTenantless();
        app.MapGet(
                "/overlap/{workspace}",
                async (HttpContext context, ITenantContextAccessor accessor) =>
                {
                    if (Interlocked.Increment(ref observations.OverlappingRequests) == 2)
                        observations.BothEntered.TrySetResult();
                    await observations.BothEntered.Task.WaitAsync(
                        TimeSpan.FromSeconds(10),
                        context.RequestAborted
                    );
                    return Read(context, accessor);
                }
            )
            .AllowAnonymous();
        app.MapControllers();
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        string Read(HttpContext context, ITenantContextAccessor accessor)
        {
            observations.EndpointCalls.AddOrUpdate(
                context.Request.Path.Value!,
                1,
                (_, count) => count + 1
            );
            return Snapshot(accessor);
        }
    }

    internal static string Snapshot(ITenantContextAccessor accessor) =>
        accessor.Current.Tenant?.Value ?? "tenantless";

    internal static bool IsPublished(HttpContext context)
    {
        try
        {
            _ = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static HttpRequestMessage Request(
        WebApplication app,
        string path,
        params (string Scheme, string Subject)[] identities
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        var cookies = identities.Select(identity =>
        {
            var options = app
                .Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(identity.Scheme);
            var principal = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("sub", identity.Subject)], identity.Scheme)
            );
            string ticket = options.TicketDataFormat.Protect(
                new AuthenticationTicket(principal, identity.Scheme)
            );
            return options.Cookie.Name + "=" + ticket;
        });
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        return request;
    }
}

internal sealed class RequestObservations
{
    internal ConcurrentDictionary<string, int> MappingCalls { get; } = new();
    internal ConcurrentDictionary<string, int> EndpointCalls { get; } = new();
    internal ConcurrentQueue<string?> Principals { get; } = new();
    internal string? BaseDomain { get; init; }
    internal bool FromUser { get; init; }
    internal Func<HttpContext, CancellationToken, ValueTask<TenantContext?>>? Resolve { get; init; }
    internal Action<HttpContext>? BeforeTenant { get; init; }
    internal bool FailurePublished { get; set; }
    internal TaskCompletionSource<bool> Cancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource BothEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int OverlappingRequests;
}

internal sealed class FixtureResolver(RequestObservations observations)
    : IHttpTenantContextResolver,
        IHttpTenantCandidateResolver
{
    public ValueTask<TenantContext?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        string? candidate = httpContext.User.FindFirstValue("sub") switch
        {
            "user-a" => "acme",
            "user-b" => "beta",
            _ => null,
        };
        return ResolveAsync(candidate, httpContext, cancellationToken);
    }

    public ValueTask<TenantContext?> ResolveAsync(
        string? candidate,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        observations.MappingCalls.AddOrUpdate(
            httpContext.Request.Path.Value!,
            1,
            (_, count) => count + 1
        );
        observations.Principals.Enqueue(httpContext.User.FindFirstValue("sub"));
        if (observations.Resolve is not null)
            return observations.Resolve(httpContext, cancellationToken);
        return ValueTask.FromResult<TenantContext?>(
            candidate?.ToLowerInvariant() switch
            {
                "acme" => TenantContext.ForTenant(new TenantId("tenant-alpha")),
                "beta" => TenantContext.ForTenant(new TenantId("tenant-beta")),
                null => TenantContext.Tenantless(),
                _ => null,
            }
        );
    }
}
