using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Api;
using Gloaming.Samples.Wholesale.Api.Endpoints;
using Gloaming.Samples.Wholesale.AppHost;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Npgsql;

namespace Gloaming.Samples.Wholesale.RuntimeComposition.Tests;

public sealed class BrowserTests
{
    private const string AlphaProfile =
        "/organizations/north-supply/customers/10000000-0000-0000-0000-000000000001/profile";
    private const string BetaProfile =
        "/organizations/south-supply/customers/10000000-0000-0000-0000-000000000002/profile";
    private const string UserPassword = "disposable-browser-user-password";
    private static readonly string[] LocalArguments =
    [
        "LocalDevelopment:UseDataVolume=false",
        "LocalDevelopment:UseLocalIdentityProvider=true",
    ];
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory(Timeout = 240_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlphaLogsInSelectsTwoTenantsAndCommitsProtectedProfileEdit(bool rowSecurity)
    {
        await using var builder = await CreateBuilderAsync(
            TestContext.Current.CancellationToken,
            rowSecurity
        );
        await using var app = await StartAndInitializeAsync(builder);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        context.SetDefaultTimeout(15_000);
        var page = await context.NewPageAsync();
        Uri endpoint = app.GetEndpoint("api", "https");
        await page.GotoAsync(new Uri(endpoint, "/public-identity").ToString()).WaitAsync(Token);
        Assert.Equal(
            42,
            (await GetAsync<CatalogResponse>(page, "/organizations/north-supply/catalog"))
                .Stock
                .AvailableQuantity
        );
        foreach (string path in new[] { AlphaProfile, "/antiforgery" })
        {
            var denied = await FetchAsync(page, path);
            Assert.Equal(401, denied.Status);
            Assert.False(denied.Redirected);
        }
        await LoginAsync(page, endpoint, "alpha");
        Assert.Equal(
            new IdentityResponse(ActorKind.Human, "application-alpha"),
            await GetAsync<IdentityResponse>(page, "/identity")
        );
        Assert.Equal(
            new TenantIdentityResponse(ActorKind.Human, "application-alpha", "wholesale-alpha"),
            await GetAsync<TenantIdentityResponse>(page, "/organizations/north-supply/identity")
        );
        Assert.Equal(
            new TenantIdentityResponse(ActorKind.Human, "application-alpha", "wholesale-beta"),
            await GetAsync<TenantIdentityResponse>(page, "/organizations/south-supply/identity")
        );
        CustomerProfile original = await GetAsync<CustomerProfile>(page, AlphaProfile);
        CustomerProfile beta = await GetAsync<CustomerProfile>(page, BetaProfile);
        var edit = new ProfileEdit(
            original.AddressId,
            original.Version,
            "Browser Updated Customer",
            "Browser Updated Street"
        );
        Assert.Equal(400, (await FetchAsync(page, AlphaProfile, "PUT", edit)).Status);
        Assert.Equal(original, await GetAsync<CustomerProfile>(page, AlphaProfile));
        var protection = await GetAsync<AntiforgeryResponse>(page, "/antiforgery");
        var changed = await FetchAsync(page, AlphaProfile, "PUT", edit, protection.RequestToken);
        Assert.Equal(200, changed.Status);
        CustomerProfile expected = original with
        {
            DisplayName = edit.DisplayName,
            AddressLine = edit.AddressLine,
            Version = original.Version + 1,
        };
        Assert.Equal(
            expected,
            changed.Body.Deserialize<CustomerProfile>(JsonSerializerOptions.Web)
        );
        Assert.Equal(expected, await GetAsync<CustomerProfile>(page, AlphaProfile));
        Assert.Equal(beta, await GetAsync<CustomerProfile>(page, BetaProfile));
        Assert.Equal(
            409,
            (await FetchAsync(page, AlphaProfile, "PUT", edit, protection.RequestToken)).Status
        );
        Assert.Equal(expected, await GetAsync<CustomerProfile>(page, AlphaProfile));
        var cookies = await context.CookiesAsync([endpoint.ToString()]);
        var identityCookie = Assert.Single(cookies, cookie => cookie.Name == "wholesale.identity");
        Assert.True(identityCookie.Secure);
        Assert.True(identityCookie.HttpOnly);
        Assert.Equal(SameSiteAttribute.Lax, identityCookie.SameSite);
        var csrfCookie = Assert.Single(cookies, cookie => cookie.Name == "wholesale.csrf");
        Assert.True(csrfCookie.Secure);
        Assert.True(csrfCookie.HttpOnly);
        Assert.Equal(SameSiteAttribute.Strict, csrfCookie.SameSite);
        if (rowSecurity)
        {
            await using var database = new NpgsqlConnection(
                await app.GetConnectionStringAsync("wholesale", Token)
            );
            await database.OpenAsync(Token);
            await using var audit = new NpgsqlCommand(
                """
                SELECT (SELECT count(*) FROM sales.audit_entries) = 1
                    AND NOT has_table_privilege('wholesale_sales', 'sales.outbox_messages', 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
                    AND EXISTS (SELECT 1 FROM sales.audit_entries WHERE tenant_key = 'wholesale-alpha'
                        AND actor_kind = 2 AND actor_key = 'application-alpha'
                        AND action = 'customer-profile.changed' AND (details->>'Version')::bigint = 2)
                    AND EXISTS (SELECT 1 FROM sales.customer_profiles WHERE organization_key = 'wholesale-beta' AND version = 1)
                    AND EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND usename = 'wholesale_sales')
                """,
                database
            );
            Assert.Equal(true, await audit.ExecuteScalarAsync(Token));
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task BetaAdmissionUsesCurrentMembershipWithTheSameBrowserSession()
    {
        await using var builder = await CreateBuilderAsync(TestContext.Current.CancellationToken);
        await using var app = await StartAndInitializeAsync(builder);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        context.SetDefaultTimeout(15_000);
        var page = await context.NewPageAsync();
        Uri endpoint = app.GetEndpoint("api", "https");
        await LoginAsync(page, endpoint, "beta");
        Assert.Equal(
            new IdentityResponse(ActorKind.Human, "application-beta"),
            await GetAsync<IdentityResponse>(page, "/identity")
        );
        Assert.Equal(
            new TenantIdentityResponse(ActorKind.Human, "application-beta", "wholesale-beta"),
            await GetAsync<TenantIdentityResponse>(page, "/organizations/south-supply/identity")
        );
        _ = await GetAsync<CustomerProfile>(page, BetaProfile);
        var cookies = await context.CookiesAsync([endpoint.ToString()]);
        string session = Assert
            .Single(cookies, cookie => cookie.Name == "wholesale.identity")
            .Value;
        await ExecuteAsync(
            app,
            "ALTER TABLE sales.customer_profiles RENAME TO unavailable_profiles"
        );
        Assert.Equal(404, (await FetchAsync(page, AlphaProfile)).Status);
        await ExecuteAsync(
            app,
            "UPDATE access.memberships SET status = 2 WHERE user_id = 'application-beta' AND organization_id = 'wholesale-beta'"
        );
        Assert.Equal(404, (await FetchAsync(page, BetaProfile)).Status);
        Assert.Equal(
            new IdentityResponse(ActorKind.Human, "application-beta"),
            await GetAsync<IdentityResponse>(page, "/identity")
        );
        cookies = await context.CookiesAsync([endpoint.ToString()]);
        Assert.Equal(
            session,
            Assert.Single(cookies, cookie => cookie.Name == "wholesale.identity").Value
        );
    }

    [Fact(Timeout = 240_000)]
    public async Task RealUnmappedAccountIsRejectedWithoutEmailLinkingOrProvisioning()
    {
        await using var builder = await CreateBuilderAsync(TestContext.Current.CancellationToken);
        await using var app = await StartAndInitializeAsync(builder);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        context.SetDefaultTimeout(15_000);
        var page = await context.NewPageAsync();
        await LoginAsync(page, app.GetEndpoint("api", "https"), "unmapped");
        foreach (
            string path in new[]
            {
                "/identity",
                "/public-identity",
                "/organizations/north-supply/catalog",
            }
        )
            Assert.Equal(403, (await FetchAsync(page, path)).Status);
        await using var connection = new NpgsqlConnection(
            await app.GetConnectionStringAsync("wholesale", Token)
        );
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM access.users) = 2 AND (SELECT count(*) FROM access.external_identities) = 2",
            connection
        );
        Assert.Equal(true, await command.ExecuteScalarAsync(Token));
    }

    private static async Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(
        CancellationToken cancellation,
        bool rowSecurity = false
    )
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<AppHostMarker>(
            [.. LocalArguments, $"LocalDevelopment:UseSalesRowSecurity={rowSecurity}"],
            cancellation
        );
        builder.Configuration["Parameters:postgres-password"] =
            "disposable-browser-database-password";
        builder.Configuration["Parameters:keycloak-password"] = "disposable-browser-admin-password";
        builder.Configuration["Parameters:oidc-client-secret"] = "disposable-browser-client-secret";
        builder.Configuration["Parameters:demo-user-password"] = UserPassword;
        builder.Configuration["Parameters:access-runtime-password"] =
            "browser access;\"quoted'password";
        builder.Configuration["Parameters:sales-runtime-password"] =
            "browser sales;\"quoted'password";
        builder.Configuration["DcpPublisher:RandomizePorts"] = "true";
        return builder;
    }

    private static async Task<DistributedApplication> StartAndInitializeAsync(
        IDistributedApplicationTestingBuilder builder
    )
    {
        var app = await builder.BuildAsync(Token);
        try
        {
            await app.StartAsync(Token);
            await app.ResourceNotifications.WaitForResourceHealthyAsync("keycloak", Token);
            Uri provider = app.GetEndpoint("keycloak", "http");
            Assert.Equal(Uri.UriSchemeHttps, provider.Scheme);
            bool rowSecurity = bool.Parse(
                builder.Configuration["LocalDevelopment:UseSalesRowSecurity"]!
            );
            if (!rowSecurity)
                await app.ResourceNotifications.WaitForResourceAsync(
                    "api",
                    KnownResourceStates.Running,
                    Token
                );
            var commands = app.Services.GetRequiredService<ResourceCommandService>();
            var result = await commands.ExecuteCommandAsync(
                "demo-setup",
                KnownResourceCommands.StartCommand,
                Token
            );
            Assert.True(result.Success, result.Message);
            await app.ResourceNotifications.WaitForResourceAsync(
                "demo-setup",
                KnownResourceStates.Finished,
                Token
            );
            Assert.True(app.ResourceNotifications.TryGetCurrentState("demo-setup", out var setup));
            Assert.Equal(0, setup.Snapshot.ExitCode);
            if (rowSecurity)
            {
                await using (
                    var database = new NpgsqlConnection(
                        await app.GetConnectionStringAsync("wholesale", Token)
                    )
                )
                {
                    await database.OpenAsync(Token);
                    await using var policies = new NpgsqlCommand(
                        """
                        SELECT ARRAY(SELECT rolname::text FROM pg_roles
                                WHERE rolname IN ('wholesale_access', 'wholesale_sales') ORDER BY rolname),
                            ARRAY(SELECT c.relname::text FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                                WHERE n.nspname = 'sales' AND c.relrowsecurity AND c.relforcerowsecurity
                                ORDER BY c.relname)
                        """,
                        database
                    );
                    await using var policiesReader = await policies.ExecuteReaderAsync(Token);
                    Assert.True(await policiesReader.ReadAsync(Token));
                    Assert.Equal<string>(
                        ["wholesale_access", "wholesale_sales"],
                        policiesReader.GetFieldValue<string[]>(0)
                    );
                    Assert.Equal<string>(
                        [
                            "audit_entries",
                            "customer_addresses",
                            "customer_profiles",
                            "draft_order_grants",
                            "draft_order_lines",
                            "draft_orders",
                        ],
                        policiesReader.GetFieldValue<string[]>(1)
                    );
                }

                var api = await commands.ExecuteCommandAsync(
                    "api",
                    KnownResourceCommands.StartCommand,
                    Token
                );
                Assert.True(api.Success, api.Message);
            }

            await app.ResourceNotifications.WaitForResourceHealthyAsync("api", Token);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static async Task LoginAsync(IPage page, Uri endpoint, string username)
    {
        var callbacks = new List<int>();
        void ObserveCallback(object? sender, IResponse response)
        {
            if (
                new Uri(response.Url).GetLeftPart(UriPartial.Path)
                == new Uri(endpoint, "/signin-oidc").ToString()
            )
                callbacks.Add(response.Status);
        }
        page.Response += ObserveCallback;
        try
        {
            await page.GotoAsync(new Uri(endpoint, "/login").ToString()).WaitAsync(Token);
            await page.Locator("#username").FillAsync(username).WaitAsync(Token);
            await page.Locator("#password").FillAsync(UserPassword).WaitAsync(Token);
            Task returned = page.WaitForURLAsync(new Uri(endpoint, "/identity").ToString());
            await page.Locator("#kc-login").ClickAsync().WaitAsync(Token);
            await returned.WaitAsync(Token);
            Assert.Equal(302, Assert.Single(callbacks));
        }
        finally
        {
            page.Response -= ObserveCallback;
        }
    }

    private static async Task<T> GetAsync<T>(IPage page, string path)
    {
        var response = await FetchAsync(page, path);
        Assert.Equal(200, response.Status);
        return response.Body.Deserialize<T>(JsonSerializerOptions.Web)!;
    }

    private static async Task<BrowserResponse> FetchAsync(
        IPage page,
        string path,
        string method = "GET",
        object? body = null,
        string? token = null
    )
    {
        JsonElement response = await page.EvaluateAsync<JsonElement>(
                """
                async ({path, method, body, token}) => {
                    const headers = {};
                    if (body !== null) headers['Content-Type'] = 'application/json';
                    if (token !== null) headers['X-CSRF-TOKEN'] = token;
                    const response = await fetch(path, {
                        method, headers, body: body === null ? undefined : JSON.stringify(body),
                        signal: AbortSignal.timeout(15000)
                    });
                    const text = await response.text();
                    return {status: response.status, redirected: response.redirected, body: text ? JSON.parse(text) : null};
                }
                """,
                new
                {
                    path,
                    method,
                    body,
                    token,
                }
            )
            .WaitAsync(Token);
        return new BrowserResponse(
            response.GetProperty("status").GetInt32(),
            response.GetProperty("redirected").GetBoolean(),
            response.GetProperty("body")
        );
    }

    private static async Task ExecuteAsync(DistributedApplication app, string sql)
    {
        await using var connection = new NpgsqlConnection(
            await app.GetConnectionStringAsync("wholesale", Token)
        );
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Token);
    }

    private sealed record BrowserResponse(int Status, bool Redirected, JsonElement Body);
}
