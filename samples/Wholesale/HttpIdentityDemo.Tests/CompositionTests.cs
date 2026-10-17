using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Gloaming.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModulithFoundry.Samples.Wholesale.Access.Persistence;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Tests;

public sealed partial class CompositionTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData("https://identity.test", "application-alpha")]
    [InlineData("https://other-identity.test", "application-beta")]
    public async Task ValidatedExternalPairMapsToApplicationUserWithoutUsingEmail(
        string issuer,
        string user
    )
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = Request(app, "/identity", issuer);
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            new IdentityResponse(ActorKind.Human, user),
            await response.Content.ReadFromJsonAsync<IdentityResponse>(Token)
        );
        using var publicRequest = Request(app, "/public-identity", issuer);
        using var publicResponse = await client.SendAsync(publicRequest, Token);
        Assert.Equal(
            new IdentityResponse(ActorKind.Human, user),
            await publicResponse.Content.ReadFromJsonAsync<IdentityResponse>(Token)
        );
    }

    [Fact]
    public async Task AnonymousPublicIdentityIsExplicitAndProtectedIdentityKeepsNativeChallenge()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var anonymous = await client.GetAsync("/public-identity", Token);
        Assert.Equal(
            new IdentityResponse(ActorKind.Anonymous, null),
            await anonymous.Content.ReadFromJsonAsync<IdentityResponse>(Token)
        );
        using var protectedResponse = await client.GetAsync("/identity", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
    }

    [Theory]
    [InlineData("/identity", false)]
    [InlineData("/public-identity", false)]
    [InlineData("/public-identity", true)]
    [InlineData("/organizations/north-supply/catalog", false)]
    public async Task UnknownOrAmbiguousExternalIdentityUsesConsumerMappingRejection(
        string path,
        bool duplicateSubject
    )
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = Request(
            app,
            path,
            duplicateSubject ? "https://identity.test" : "https://unknown.test",
            duplicateSubject
        );
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(403, problem!.Status);
        Assert.Equal("Actor mapping failed.", problem.Title);
    }

    [Fact]
    public async Task SameActorSelectsDifferentOrganizationsInSeparateRequests()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        foreach (
            var (organization, tenant) in new[]
            {
                ("north-supply", "wholesale-alpha"),
                ("south-supply", "wholesale-beta"),
            }
        )
        {
            using var request = Request(
                app,
                $"/organizations/{organization}/identity",
                "https://identity.test"
            );
            using var response = await client.SendAsync(request, Token);
            response.EnsureSuccessStatusCode();
            Assert.Equal(
                new TenantIdentityResponse(ActorKind.Human, "application-alpha", tenant),
                await response.Content.ReadFromJsonAsync<TenantIdentityResponse>(Token)
            );
        }
    }

    [Theory]
    [InlineData("north-supply", "wholesale-alpha", 42)]
    [InlineData("south-supply", "wholesale-beta", 7)]
    public async Task AnonymousCatalogReadsUseSelectedTenantAndInventoryContract(
        string organization,
        string tenant,
        int quantity
    )
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        var result = await client.GetFromJsonAsync<CatalogResponse>(
            $"/organizations/{organization}/catalog",
            Token
        );
        Assert.Equal(
            new CatalogResponse(
                new TenantIdentityResponse(ActorKind.Anonymous, null, tenant),
                new StockAvailability("DEMO-NOTEBOOK", quantity)
            ),
            result
        );
    }

    [Theory]
    [InlineData("NORTH-SUPPLY.WHOLESALE.EXAMPLE.TEST.:8443", "wholesale-alpha", 42)]
    [InlineData("south-supply.wholesale.example.test", "wholesale-beta", 7)]
    public async Task ConfiguredSubdomainAlternativeUsesSameCanonicalRegistry(
        string host,
        string tenant,
        int quantity
    )
    {
        await using var app = await StartAsync(subdomain: true);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/catalog");
        request.Headers.Host = host;
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            new CatalogResponse(
                new TenantIdentityResponse(ActorKind.Anonymous, null, tenant),
                new StockAvailability("DEMO-NOTEBOOK", quantity)
            ),
            await response.Content.ReadFromJsonAsync<CatalogResponse>(Token)
        );
    }

    [Theory]
    [InlineData("wholesale.example.test", 400)]
    [InlineData("unknown.wholesale.example.test", 404)]
    [InlineData("nested.north-supply.wholesale.example.test", 404)]
    [InlineData("north-supply.other.example.test", 400)]
    public async Task SubdomainSelectionAndNativeHostFilteringRejectUnsafeCatalogRequests(
        string host,
        int status
    )
    {
        await using var app = await StartAsync(subdomain: true);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/catalog");
        request.Headers.Host = host;
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(status, (int)response.StatusCode);
        if (host != "north-supply.other.example.test")
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Token);
            Assert.Equal(status, problem!.Status);
            Assert.Equal(
                status == 400 ? "Select an Organization." : "Organization selection failed.",
                problem.Title
            );
        }
    }

    [Fact]
    public async Task UnknownOrganizationGetsConsumerFailureWhileProtectedReadKeepsNativeChallenge()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var unknown = await client.GetAsync("/organizations/unknown/catalog", Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains(
            "Organization selection failed.",
            await unknown.Content.ReadAsStringAsync(Token)
        );
        using var protectedResponse = await client.GetAsync(
            "/organizations/north-supply/identity",
            Token
        );
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        using var healthy = await client.GetAsync("/health", Token);
        healthy.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InventoryContractRequiresTenantForCallsOutsideHttp(bool tenantless)
    {
        await using var app = await StartAsync();
        using var scope = app.Services.CreateScope();
        if (tenantless)
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
        var catalog = scope.ServiceProvider.GetRequiredService<IStockCatalog>();
        if (tenantless)
            await Assert.ThrowsAsync<TenantRequiredException>(() =>
                catalog.ReadAsync("DEMO-NOTEBOOK", Token)
            );
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                catalog.ReadAsync("DEMO-NOTEBOOK", Token)
            );
    }

    private async Task<WebApplication> StartAsync(
        bool subdomain = false,
        Action<WebApplication>? beforeContext = null,
        Action<WebApplication>? afterContext = null,
        string? connectionString = null,
        bool initialize = true,
        Action<WebApplicationBuilder>? configureHost = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.WebHost.UseTestServer();
        connectionString ??= await postgres.CreateDatabaseAsync(Token);
        // Each host owns a disposable database; retaining a pool per database exhausts the shared test server.
        connectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
        }.ConnectionString;
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Oidc:Authority"] = "https://identity.test",
                ["Oidc:ClientId"] = "configured-demo",
                ["ConnectionStrings:Access"] = connectionString,
            }
        );
        configureHost?.Invoke(builder);
        DemoComposition.AddServices(builder.Services, builder.Configuration);
        if (subdomain)
        {
            builder.Services.AddOrganizationTenancyFromSubdomain("wholesale.example.test.");
            builder.Services.AddHostFiltering(options =>
                options.UseTenantSubdomainHosts("wholesale.example.test.")
            );
        }
        else
            builder.Services.AddOrganizationTenancyFromRoute("organization");
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        var app = builder.Build();
        try
        {
            if (initialize)
            {
                await using var setup = app.Services.CreateAsyncScope();
                await setup
                    .ServiceProvider.GetRequiredService<AccessDbContext>()
                    .Database.MigrateAsync(Token);
                await setup
                    .ServiceProvider.GetRequiredService<InventoryDbContext>()
                    .Database.MigrateAsync(Token);
                await setup
                    .ServiceProvider.GetRequiredService<SalesDbContext>()
                    .Database.MigrateAsync(Token);
                foreach (string organization in new[] { "wholesale-alpha", "wholesale-beta" })
                {
                    await using var seedScope = app.Services.CreateAsyncScope();
                    seedScope
                        .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                        .Initialize(TenantContext.ForTenant(new TenantId(organization)));
                    var sales = seedScope.ServiceProvider.GetRequiredService<SalesDbContext>();
                    SalesDemoSeed.Stage(sales);
                    DraftOrderAccessDemoSeed.Stage(sales);
                    await sales.SaveChangesAsync(Token);
                }
                await ExecuteAsync(
                    app,
                    """
                    INSERT INTO inventory.stock_availability (id, organization_key, sku, available_quantity) VALUES
                        (gen_random_uuid(), 'wholesale-alpha', 'DEMO-NOTEBOOK', 42),
                        (gen_random_uuid(), 'wholesale-beta', 'DEMO-NOTEBOOK', 7),
                        (gen_random_uuid(), 'wholesale-beta', 'BETA-ONLY', 13);
                    INSERT INTO access.users (id) VALUES ('application-alpha'), ('application-beta');
                    INSERT INTO access.external_identities (issuer, subject, user_id) VALUES
                        ('https://identity.test', 'shared-subject', 'application-alpha'),
                        ('https://other-identity.test', 'shared-subject', 'application-beta'),
                        ('https://linked-identity.test', 'shared-subject', 'application-alpha');
                    INSERT INTO access.organizations (id, slug) VALUES
                        ('wholesale-alpha', 'north-supply'), ('wholesale-beta', 'south-supply');
                    INSERT INTO access.memberships (id, organization_id, user_id, status) VALUES
                        (gen_random_uuid(), 'wholesale-alpha', 'application-alpha', 1),
                        (gen_random_uuid(), 'wholesale-beta', 'application-alpha', 1),
                        (gen_random_uuid(), 'wholesale-beta', 'application-beta', 1);
                    """
                );
            }
            beforeContext?.Invoke(app);
            DemoComposition.ConfigureHttp(app);
            afterContext?.Invoke(app);
            DemoComposition.MapOrganizationEndpoints(
                app,
                subdomain ? "/catalog" : "/organizations/{organization}/catalog",
                subdomain ? "/tenant-identity" : "/organizations/{organization}/identity",
                subdomain ? "/stock/{sku}" : "/organizations/{organization}/stock/{sku}"
            );
            CustomerProfileEndpoints.Map(
                app,
                subdomain
                    ? "/customers/{customerId:guid}/profile"
                    : "/organizations/{organization}/customers/{customerId:guid}/profile"
            );
            MembershipEndpoints.Map(
                app,
                subdomain
                    ? "/memberships/{membershipId:guid}"
                    : "/organizations/{organization}/memberships/{membershipId:guid}"
            );
            DraftOrderEndpoints.Map(
                app,
                subdomain ? "/draft-orders" : "/organizations/{organization}/draft-orders"
            );
            if (app.Configuration.GetConnectionString("Sales") is { } salesRuntime)
                await SalesHttpRowSecurity.VerifyRuntimeAsync(
                    salesRuntime,
                    app.Configuration.GetConnectionString("Access")!,
                    Token
                );

            await app.StartAsync(Token);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static HttpRequestMessage Request(
        WebApplication app,
        string path,
        string issuer,
        bool duplicateSubject = false
    )
    {
        var claims = new List<Claim>
        {
            new("iss", issuer),
            new("sub", "shared-subject"),
            new("email", "same-person@example.test"),
        };
        if (duplicateSubject)
            claims.Add(new Claim("sub", "another-subject"));
        var identity = new ClaimsIdentity(claims, "fixture-cookie");
        var oidc = app
            .Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get("oidc");
        using var payload = JsonSerializer.SerializeToDocument(
            new
            {
                iss = issuer,
                sub = "shared-subject",
                email = "same-person@example.test",
            }
        );
        foreach (var action in oidc.ClaimActions)
            action.Run(payload.RootElement, identity, issuer);
        var principal = new ClaimsPrincipal(identity);
        var options = app
            .Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(
            principal,
            CookieAuthenticationDefaults.AuthenticationScheme
        );
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(
            "Cookie",
            options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket)
        );
        return request;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task ExecuteAsync(
        WebApplication app,
        string sql,
        params NpgsqlParameter[] parameters
    )
    {
        await using var connection = new NpgsqlConnection(
            app.Configuration.GetConnectionString("Access")
        );
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(Token);
    }
}
