using System.Net;
using System.Net.Http.Json;
using Gloaming.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Api;
using ModulithFoundry.Samples.Wholesale.Api.Endpoints;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    private static readonly int[] CommitAndConflictStatuses = [200, 409];

    private sealed record ProtectedSession(
        string AuthenticationCookie,
        string AntiforgeryCookie,
        string RequestToken
    );

    private static string ProfilePath(
        Guid? customer = null,
        string organization = "north-supply"
    ) =>
        $"/organizations/{organization}/customers/{customer ?? SalesDemoSeed.AlphaCustomerId}/profile";

    private static HttpClient SecureClient(WebApplication app)
    {
        HttpClient client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private static async Task<ProtectedSession> IssueTokenAsync(
        WebApplication app,
        HttpClient client,
        string issuer = "https://identity.test"
    )
    {
        using var request = Request(app, "/antiforgery", issuer);
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        return new ProtectedSession(
            Assert.Single(request.Headers.GetValues("Cookie")),
            cookie.Split(';')[0],
            (await response.Content.ReadFromJsonAsync<AntiforgeryResponse>(Token))!.RequestToken
        );
    }

    private static HttpRequestMessage EditRequest(
        ProtectedSession session,
        ProfileEdit? edit = null,
        string? path = null
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path ?? ProfilePath())
        {
            Content = JsonContent.Create(
                edit
                    ?? new ProfileEdit(
                        SalesDemoSeed.AlphaAddressId,
                        1,
                        "Changed Alpha",
                        "Changed Street"
                    )
            ),
        };
        request.Headers.Add(
            "Cookie",
            session.AuthenticationCookie + "; " + session.AntiforgeryCookie
        );
        request.Headers.Add("X-CSRF-TOKEN", session.RequestToken);
        return request;
    }

    private static async Task<CustomerProfile> ReadProfileAsync(
        WebApplication app,
        HttpClient client,
        Guid? customer = null,
        string organization = "north-supply"
    )
    {
        using var request = Request(
            app,
            ProfilePath(customer, organization),
            "https://identity.test"
        );
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerProfile>(Token))!;
    }

    [Fact]
    public async Task ProtectedEditCommitsBothRowsAndTokenCanBeUsedInAnotherAdmittedOrganization()
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        CustomerProfile before = await ReadProfileAsync(app, client);
        Assert.Equal(
            new CustomerProfile(
                SalesDemoSeed.AlphaCustomerId,
                SalesDemoSeed.AlphaAddressId,
                "DEMO-CUSTOMER",
                "Alpha Customer",
                "Alpha Street",
                1
            ),
            before
        );
        var session = await IssueTokenAsync(app, client);
        using var request = EditRequest(session);
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        var expected = before with
        {
            DisplayName = "Changed Alpha",
            AddressLine = "Changed Street",
            Version = 2,
        };
        Assert.Equal(expected, await response.Content.ReadFromJsonAsync<CustomerProfile>(Token));
        Assert.Equal(expected, await ReadProfileAsync(app, client));
        CustomerProfile beta = await ReadProfileAsync(
            app,
            client,
            SalesDemoSeed.BetaCustomerId,
            "south-supply"
        );
        Assert.Equal("Beta Customer", beta.DisplayName);
        Assert.Equal("Beta Street", beta.AddressLine);
        Assert.Equal(1, beta.Version);
        using var betaRequest = EditRequest(
            session,
            new ProfileEdit(beta.AddressId, 1, "Changed Beta", "Beta Lane"),
            ProfilePath(beta.CustomerId, "south-supply")
        );
        using var betaResponse = await client.SendAsync(betaRequest, Token);
        betaResponse.EnsureSuccessStatusCode();
        Assert.Equal(
            2,
            (await ReadProfileAsync(app, client, beta.CustomerId, "south-supply")).Version
        );
    }

    [Theory]
    [InlineData("token-missing")]
    [InlineData("token-invalid")]
    [InlineData("cookie-missing")]
    [InlineData("cookie-invalid")]
    public async Task InvalidNativeAntiforgeryCannotReachSales(string fault)
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(app, client);
        var session = await IssueTokenAsync(app, client);
        // Unavailable Sales proves the rejection precedes the Contract/database operation.
        await ExecuteAsync(
            app,
            "ALTER TABLE sales.customer_profiles RENAME TO unavailable_profiles"
        );
        using var request = EditRequest(session);
        if (fault.StartsWith("token", StringComparison.Ordinal))
        {
            request.Headers.Remove("X-CSRF-TOKEN");
            if (fault == "token-invalid")
                request.Headers.Add("X-CSRF-TOKEN", "invalid");
        }
        else
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add(
                "Cookie",
                session.AuthenticationCookie
                    + (fault == "cookie-invalid" ? "; wholesale.csrf=invalid" : "")
            );
        }
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ExecuteAsync(
            app,
            "ALTER TABLE sales.unavailable_profiles RENAME TO customer_profiles"
        );
        Assert.Equal(before, await ReadProfileAsync(app, client));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenBindsApplicationActorEvenWhenExactNativePrincipalIsUnchanged(
        bool reassignProvider
    )
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(
            app,
            client,
            SalesDemoSeed.BetaCustomerId,
            "south-supply"
        );
        var session = await IssueTokenAsync(app, client);
        if (reassignProvider)
            await ExecuteAsync(
                app,
                """
                UPDATE access.external_identities SET user_id = 'application-beta'
                WHERE issuer = 'https://identity.test' AND subject = 'shared-subject'
                """
            );
        else
        {
            using var beta = Request(app, "/identity", "https://other-identity.test");
            session = session with
            {
                AuthenticationCookie = Assert.Single(beta.Headers.GetValues("Cookie")),
            };
        }
        // Both users are admitted to Beta; only the application actor binding rejects the token.
        using var identity = new HttpRequestMessage(HttpMethod.Get, "/identity");
        identity.Headers.Add("Cookie", session.AuthenticationCookie);
        using var identityResponse = await client.SendAsync(identity, Token);
        Assert.Equal(
            "application-beta",
            (await identityResponse.Content.ReadFromJsonAsync<IdentityResponse>(Token))!.ActorId
        );
        using var request = EditRequest(
            session,
            new ProfileEdit(SalesDemoSeed.BetaAddressId, 1, "Forbidden", "Forbidden"),
            ProfilePath(SalesDemoSeed.BetaCustomerId, "south-supply")
        );
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            before,
            await ReadProfileAsync(app, client, SalesDemoSeed.BetaCustomerId, "south-supply")
        );
        var fresh = await IssueTokenAsync(
            app,
            client,
            reassignProvider ? "https://identity.test" : "https://other-identity.test"
        );
        using var retry = EditRequest(
            fresh,
            new ProfileEdit(SalesDemoSeed.BetaAddressId, 1, "Fresh Beta", "Fresh Street"),
            ProfilePath(SalesDemoSeed.BetaCustomerId, "south-supply")
        );
        using var recovered = await client.SendAsync(retry, Token);
        recovered.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("foreign-customer")]
    [InlineData("foreign-address")]
    [InlineData("other-customer-address")]
    public async Task SelectedTenantAndCustomerAddressPairAreRequired(string fault)
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(app, client);
        var beta = await ReadProfileAsync(
            app,
            client,
            SalesDemoSeed.BetaCustomerId,
            "south-supply"
        );
        Guid address = SalesDemoSeed.BetaAddressId;
        if (fault == "other-customer-address")
        {
            await ExecuteAsync(
                app,
                """
                INSERT INTO sales.customer_profiles (id, organization_key, code, display_name, version)
                VALUES ('10000000-0000-0000-0000-000000000003', 'wholesale-alpha', 'SECOND', 'Second', 1);
                INSERT INTO sales.customer_addresses (id, organization_key, customer_id, address_line)
                VALUES ('20000000-0000-0000-0000-000000000003', 'wholesale-alpha',
                    '10000000-0000-0000-0000-000000000003', 'Second Street');
                """
            );
            address = Guid.Parse("20000000-0000-0000-0000-000000000003");
        }
        var session = await IssueTokenAsync(app, client);
        using var request = EditRequest(
            session,
            new ProfileEdit(address, 1, "Forbidden", "Forbidden"),
            ProfilePath(
                fault == "foreign-customer"
                    ? SalesDemoSeed.BetaCustomerId
                    : SalesDemoSeed.AlphaCustomerId
            )
        );
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await ReadProfileAsync(app, client));
        Assert.Equal(
            beta,
            await ReadProfileAsync(app, client, SalesDemoSeed.BetaCustomerId, "south-supply")
        );
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("unknown", 403)]
    [InlineData("nonmember", 404)]
    [InlineData("revoked", 404)]
    public async Task AdmissionRejectsMutationBeforeAntiforgeryOrUnavailableSales(
        string caller,
        int status
    )
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(app, client);
        var session = await IssueTokenAsync(app, client);
        if (caller == "revoked")
            await ExecuteAsync(
                app,
                "UPDATE access.memberships SET status = 2 WHERE organization_id = 'wholesale-alpha'"
            );
        await ExecuteAsync(
            app,
            "ALTER TABLE sales.customer_profiles RENAME TO unavailable_profiles"
        );
        using var request = EditRequest(session);
        request.Headers.Remove("Cookie");
        request.Headers.Remove("X-CSRF-TOKEN");
        if (caller != "anonymous")
        {
            using var auth = Request(
                app,
                "/identity",
                caller switch
                {
                    "unknown" => "https://unknown.test",
                    "nonmember" => "https://other-identity.test",
                    _ => "https://identity.test",
                }
            );
            request.Headers.Add("Cookie", Assert.Single(auth.Headers.GetValues("Cookie")));
        }
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(status, (int)response.StatusCode);
        await ExecuteAsync(
            app,
            "ALTER TABLE sales.unavailable_profiles RENAME TO customer_profiles"
        );
        if (caller == "revoked")
            await ExecuteAsync(
                app,
                "UPDATE access.memberships SET status = 1 WHERE organization_id = 'wholesale-alpha'"
            );
        Assert.Equal(before, await ReadProfileAsync(app, client));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleOrCompetingEditsCommitExactlyOneVersion(bool concurrent)
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var session = await IssueTokenAsync(app, client);
        using var first = EditRequest(
            session,
            new ProfileEdit(SalesDemoSeed.AlphaAddressId, 1, "First", "First Street")
        );
        using var second = EditRequest(
            session,
            new ProfileEdit(SalesDemoSeed.AlphaAddressId, 1, "Second", "Second Street")
        );
        await using var blocker = new NpgsqlConnection(
            app.Configuration.GetConnectionString("Access")
        );
        await blocker.OpenAsync(Token);
        await using var barrier = await blocker.BeginTransactionAsync(Token);
        if (concurrent)
        {
            await using var command = new NpgsqlCommand(
                "LOCK TABLE sales.customer_profiles IN SHARE MODE",
                blocker,
                barrier
            );
            await command.ExecuteNonQueryAsync(Token);
        }
        Task<HttpResponseMessage> firstTask = client.SendAsync(first, Token);
        if (!concurrent)
            await firstTask;
        Task<HttpResponseMessage> secondTask = client.SendAsync(second, Token);
        try
        {
            if (concurrent)
                await WaitForBlockedUpdatesAsync(app, "customer_profiles", 2);
        }
        finally
        {
            await barrier.RollbackAsync(CancellationToken.None);
        }
        using var secondResponse = await secondTask;
        using var firstResponse = await firstTask;
        Assert.Equal(
            CommitAndConflictStatuses,
            new[] { (int)firstResponse.StatusCode, (int)secondResponse.StatusCode }
                .Order()
                .ToArray()
        );
        var winner = await (
            firstResponse.IsSuccessStatusCode ? firstResponse : secondResponse
        ).Content.ReadFromJsonAsync<CustomerProfile>(Token);
        Assert.Equal(winner, await ReadProfileAsync(app, client));
        Assert.Equal(2, winner!.Version);
    }

    [Fact]
    public async Task AddressFailureAfterCustomerSaveRollsBackBothRowsAndFreshRequestRecovers()
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(app, client);
        var session = await IssueTokenAsync(app, client);
        await ExecuteAsync(
            app,
            "ALTER TABLE sales.customer_addresses ADD CONSTRAINT test_address_fault CHECK (address_line <> 'Rejected Street')"
        );
        using var failed = EditRequest(
            session,
            new ProfileEdit(before.AddressId, 1, "Should Roll Back", "Rejected Street")
        );
        using var failedResponse = await client.SendAsync(failed, Token);
        Assert.Equal(HttpStatusCode.InternalServerError, failedResponse.StatusCode);
        Assert.Equal(before, await ReadProfileAsync(app, client));
        await ExecuteAsync(
            app,
            "ALTER TABLE sales.customer_addresses DROP CONSTRAINT test_address_fault"
        );
        using var retry = EditRequest(session);
        using var response = await client.SendAsync(retry, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(2, (await ReadProfileAsync(app, client)).Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SalesContractRequiresEstablishedTenantOutsideHttp(bool tenantless)
    {
        await using var app = await StartAsync();
        await using var scope = app.Services.CreateAsyncScope();
        if (tenantless)
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
        var profiles = scope.ServiceProvider.GetRequiredService<ICustomerProfiles>();
        Type expected = tenantless
            ? typeof(TenantRequiredException)
            : typeof(InvalidOperationException);
        Assert.IsType(
            expected,
            await Record.ExceptionAsync(() =>
                profiles.ReadAsync(SalesDemoSeed.AlphaCustomerId, Token)
            )
        );
        Assert.IsType(
            expected,
            await Record.ExceptionAsync(() =>
                profiles.ChangeAsync(
                    new CustomerProfileChange(
                        SalesDemoSeed.AlphaCustomerId,
                        SalesDemoSeed.AlphaAddressId,
                        1,
                        "Change",
                        "Street"
                    ),
                    Token
                )
            )
        );
    }

    [Theory]
    [InlineData("address")]
    [InlineData("version-zero")]
    [InlineData("version-overflow")]
    [InlineData("name-blank")]
    [InlineData("name-long")]
    [InlineData("address-long")]
    public async Task InvalidProfileInputIsRejectedBeforeSave(string invalid)
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(app, client);
        var session = await IssueTokenAsync(app, client);
        var edit = new ProfileEdit(before.AddressId, 1, "Change", "Street");
        edit = invalid switch
        {
            "address" => edit with { AddressId = Guid.Empty },
            "version-zero" => edit with { ExpectedVersion = 0 },
            "version-overflow" => edit with { ExpectedVersion = long.MaxValue },
            "name-blank" => edit with { DisplayName = " " },
            "name-long" => edit with { DisplayName = new string('a', 257) },
            "address-long" => edit with { AddressLine = new string('a', 513) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        using var request = EditRequest(session, edit);
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadProfileAsync(app, client));
        await using var scope = app.Services.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        var profiles = scope.ServiceProvider.GetRequiredService<ICustomerProfiles>();
        Assert.IsAssignableFrom<ArgumentException>(
            await Record.ExceptionAsync(() =>
                profiles.ChangeAsync(
                    new CustomerProfileChange(
                        before.CustomerId,
                        edit.AddressId,
                        edit.ExpectedVersion,
                        edit.DisplayName,
                        edit.AddressLine
                    ),
                    Token
                )
            )
        );
    }

    [Fact]
    public async Task SubdomainProfileUsesSameAdmittedMutationAndPublicCatalogGrantsNoProfileAuthority()
    {
        await using var app = await StartAsync(subdomain: true);
        using var client = SecureClient(app);
        client.BaseAddress = new Uri("https://north-supply.wholesale.example.test");
        using var publicRead = Request(app, "/catalog", "https://other-identity.test");
        using var publicResponse = await client.SendAsync(publicRead, Token);
        publicResponse.EnsureSuccessStatusCode();
        using var profileRead = Request(
            app,
            $"/customers/{SalesDemoSeed.AlphaCustomerId}/profile",
            "https://other-identity.test"
        );
        using var denied = await client.SendAsync(profileRead, Token);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var session = await IssueTokenAsync(app, client);
        using var edit = EditRequest(
            session,
            path: $"/customers/{SalesDemoSeed.AlphaCustomerId}/profile"
        );
        using var edited = await client.SendAsync(edit, Token);
        edited.EnsureSuccessStatusCode();
        Assert.Equal(2, (await edited.Content.ReadFromJsonAsync<CustomerProfile>(Token))!.Version);
    }
}
