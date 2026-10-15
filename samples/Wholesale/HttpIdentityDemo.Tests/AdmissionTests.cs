using System.Net;
using System.Net.Http.Json;
using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Access.Contracts;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Tests;

public sealed partial class CompositionTests
{
    [Fact]
    public async Task PrelinkedExternalAccountRetainsTheGlobalUser()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = Request(app, "/identity", "https://linked-identity.test");
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            new IdentityResponse(ActorKind.Human, "application-alpha"),
            await response.Content.ReadFromJsonAsync<IdentityResponse>(Token)
        );
    }

    [Theory]
    [InlineData("https://IDENTITY.test")]
    [InlineData("https://identity.test/")]
    public async Task IssuerKeyIsExactRatherThanHostnameCanonicalized(string issuer)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = Request(app, "/identity", issuer);
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticatedNonMemberCanReadPublicCatalogButCannotEnterProtectedOrganization(
        bool subdomain
    )
    {
        await using var app = await StartAsync(subdomain: subdomain);
        using var client = app.GetTestClient();
        using var publicRequest = Request(
            app,
            subdomain ? "/catalog" : "/organizations/north-supply/catalog",
            "https://other-identity.test"
        );
        if (subdomain)
            publicRequest.Headers.Host = "NORTH-SUPPLY.wholesale.example.test";
        using var response = await client.SendAsync(publicRequest, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            new CatalogResponse(
                new TenantIdentityResponse(ActorKind.Human, "application-beta", "wholesale-alpha"),
                new StockAvailability("DEMO-NOTEBOOK", 42)
            ),
            await response.Content.ReadFromJsonAsync<CatalogResponse>(Token)
        );

        using var denied = Request(
            app,
            subdomain ? "/tenant-identity" : "/organizations/north-supply/identity",
            "https://other-identity.test"
        );
        if (subdomain)
            denied.Headers.Host = "north-supply.wholesale.example.test";
        using var denial = await client.SendAsync(denied, Token);
        Assert.Equal(HttpStatusCode.NotFound, denial.StatusCode);
        using var allowed = Request(
            app,
            subdomain ? "/tenant-identity" : "/organizations/south-supply/identity",
            "https://other-identity.test"
        );
        if (subdomain)
            allowed.Headers.Host = "SOUTH-SUPPLY.wholesale.example.test.";
        using var admission = await client.SendAsync(allowed, Token);
        admission.EnsureSuccessStatusCode();
        Assert.Equal(
            new TenantIdentityResponse(ActorKind.Human, "application-beta", "wholesale-beta"),
            await admission.Content.ReadFromJsonAsync<TenantIdentityResponse>(Token)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MissingSuspendedAndRemovedMembershipDenyWithoutPublishingTenant(int status)
    {
        bool published = true;
        await using var app = await StartAsync(beforeContext: app =>
            ObservePublication(app, value => published = value)
        );
        await ExecuteAsync(
            app,
            status == 0
                ? "DELETE FROM access.memberships WHERE user_id = 'application-alpha' AND organization_id = 'wholesale-alpha'"
                : "UPDATE access.memberships SET status = @status WHERE user_id = 'application-alpha' AND organization_id = 'wholesale-alpha'",
            status == 0 ? [] : [new Npgsql.NpgsqlParameter("status", status)]
        );
        using var client = app.GetTestClient();
        using var request = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var response = await client.SendAsync(request, Token);
        Assert.False(published);
        var denial = await response.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var unknown = Request(
            app,
            "/organizations/unknown/identity",
            "https://identity.test"
        );
        using var unknownResponse = await client.SendAsync(unknown, Token);
        var missing = await unknownResponse.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Equal(denial!.Status, missing!.Status);
        Assert.Equal(denial.Title, missing.Title);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RevocationBlocksNextAdmissionWithSameCookieButKeepsGlobalIdentity(int status)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var first = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        string cookie = first.Headers.GetValues("Cookie").Single();
        using var admitted = await client.SendAsync(first, Token);
        admitted.EnsureSuccessStatusCode();
        await ExecuteAsync(
            app,
            "UPDATE access.memberships SET status = @status WHERE user_id = 'application-alpha' AND organization_id = 'wholesale-alpha'",
            new Npgsql.NpgsqlParameter("status", status)
        );
        using var next = new HttpRequestMessage(
            HttpMethod.Get,
            "/organizations/north-supply/identity"
        );
        next.Headers.Add("Cookie", cookie);
        using var denied = await client.SendAsync(next, Token);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var identity = new HttpRequestMessage(HttpMethod.Get, "/identity");
        identity.Headers.Add("Cookie", cookie);
        using var mapped = await client.SendAsync(identity, Token);
        Assert.Equal(
            new IdentityResponse(ActorKind.Human, "application-alpha"),
            await mapped.Content.ReadFromJsonAsync<IdentityResponse>(Token)
        );
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AlreadyAdmittedOperationRetainsContextWhenMembershipIsSuspendedOrRemoved(
        int status
    )
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartAsync(afterContext: app =>
            app.Use(
                async (context, next) =>
                {
                    if (context.Request.Path == "/organizations/north-supply/identity")
                    {
                        admitted.TrySetResult();
                        await resume.Task.WaitAsync(Token);
                    }
                    await next(context);
                }
            )
        );
        using var client = app.GetTestClient();
        using var request = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        Task<HttpResponseMessage> operation = client.SendAsync(request, Token);
        try
        {
            await admitted.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await ExecuteAsync(
                app,
                "UPDATE access.memberships SET status = @status WHERE user_id = 'application-alpha' AND organization_id = 'wholesale-alpha'",
                new Npgsql.NpgsqlParameter("status", status)
            );
        }
        finally
        {
            resume.TrySetResult();
        }
        using var response = await operation;
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            new TenantIdentityResponse(ActorKind.Human, "application-alpha", "wholesale-alpha"),
            await response.Content.ReadFromJsonAsync<TenantIdentityResponse>(Token)
        );
        using var next = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var denied = await client.SendAsync(next, Token);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task AllowAnonymousAloneDoesNotMakeOrganizationAccessPublic()
    {
        await using var app = await StartAsync(afterContext: app =>
            app.MapGet("/organizations/{organization}/member-without-auth", () => Results.Ok())
                .AllowAnonymous()
        );
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(
            "/organizations/north-supply/member-without-auth",
            Token
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NativeChallengeShortCircuitsUnavailableAdmissionDatabase()
    {
        await using var app = await StartAsync();
        await ExecuteAsync(
            app,
            "ALTER TABLE access.organizations RENAME TO organizations_unavailable"
        );
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/organizations/north-supply/identity", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Repair before exercising a fresh authenticated request.
        await ExecuteAsync(
            app,
            "ALTER TABLE access.organizations_unavailable RENAME TO organizations"
        );
        using var request = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var admitted = await client.SendAsync(request, Token);
        admitted.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DatabaseFaultDoesNotBecomeAdmissionDenialAndFreshRequestRecovers()
    {
        bool published = true;
        int downstream = 0;
        await using var app = await StartAsync(
            beforeContext: app => ObservePublication(app, value => published = value),
            afterContext: app =>
                app.Use(
                    async (context, next) =>
                    {
                        downstream++;
                        await next(context);
                    }
                )
        );
        await ExecuteAsync(app, "ALTER TABLE access.memberships RENAME TO memberships_unavailable");
        using var client = app.GetTestClient();
        using var request = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var failed = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.False(published);
        Assert.Equal(0, downstream);
        await ExecuteAsync(app, "ALTER TABLE access.memberships_unavailable RENAME TO memberships");
        using var retry = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var recovered = await client.SendAsync(retry, Token);
        recovered.EnsureSuccessStatusCode();
        Assert.True(published);
        Assert.Equal(1, downstream);
    }

    [Theory]
    [InlineData("NORTH-SUPPLY", 200)]
    [InlineData("north_supply", 404)]
    [InlineData("north%20supply", 404)]
    public async Task SlugLookupAcceptsAsciiCaseButDoesNotRewriteSeparators(
        string candidate,
        int status
    )
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync($"/organizations/{candidate}/catalog", Token);
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 200)
            Assert.Equal(
                "wholesale-alpha",
                (await response.Content.ReadFromJsonAsync<CatalogResponse>(Token))!.Context.TenantId
            );
    }

    [Fact]
    public async Task NonHttpCallerExplicitlyAdmitsAndEstablishesItsOwnTenant()
    {
        await using var app = await StartAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<IApplicationAccess>();
        UserId? user = await access.ResolveUserAsync(
            new ExternalIdentity("https://identity.test", "shared-subject"),
            Token
        );
        Assert.Equal(new UserId("application-alpha"), user);
        OrganizationId? organization = await access.ResolveMemberOrganizationAsync(
            user!,
            "north-supply",
            Token
        );
        Assert.Equal(new OrganizationId("wholesale-alpha"), organization);
        var tenancy = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        Assert.Throws<InvalidOperationException>(() => tenancy.Current);
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization!.Value)));
        Assert.Equal(
            new StockAvailability("DEMO-NOTEBOOK", 42),
            await scope
                .ServiceProvider.GetRequiredService<IStockCatalog>()
                .ReadAsync("DEMO-NOTEBOOK", Token)
        );
    }

    private static void ObservePublication(WebApplication app, Action<bool> observe) =>
        app.Use(
            async (context, next) =>
            {
                var tenant = context.RequestServices.GetRequiredService<ITenantContextAccessor>();
                try
                {
                    await next(context);
                }
                finally
                {
                    try
                    {
                        _ = tenant.Current;
                        observe(true);
                    }
                    catch (InvalidOperationException)
                    {
                        observe(false);
                    }
                }
            }
        );
}
