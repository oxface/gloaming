using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Access.Contracts;
using ModulithFoundry.Samples.Wholesale.Access.Persistence;
using ModulithFoundry.Samples.Wholesale.Api;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdmittedStockReadsUsePersistedTenantRowsAndObserveFreshChanges(bool subdomain)
    {
        await using var app = await StartAsync(subdomain: subdomain);
        using var client = app.GetTestClient();
        foreach (
            var (slug, tenant, quantity) in new[]
            {
                ("north-supply", "wholesale-alpha", 42),
                ("south-supply", "wholesale-beta", 7),
                ("north-supply", "wholesale-alpha", 42),
            }
        )
        {
            string path = subdomain
                ? "/stock/DEMO-NOTEBOOK"
                : $"/organizations/{slug}/stock/DEMO-NOTEBOOK";
            using var request = Request(app, path, "https://identity.test");
            if (subdomain)
                request.Headers.Host = slug + ".wholesale.example.test";
            using var response = await client.SendAsync(request, Token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CatalogResponse>(Token);
            Assert.Equal("application-alpha", result!.Context.ActorId);
            Assert.Equal(tenant, result.Context.TenantId);
            Assert.Equal(new StockAvailability("DEMO-NOTEBOOK", quantity), result.Stock);
        }
        await ExecuteAsync(
            app,
            "UPDATE inventory.stock_availability SET available_quantity = 31 WHERE organization_key = 'wholesale-alpha'"
        );
        using var fresh = Request(
            app,
            subdomain ? "/stock/DEMO-NOTEBOOK" : "/organizations/north-supply/stock/DEMO-NOTEBOOK",
            "https://identity.test"
        );
        if (subdomain)
            fresh.Headers.Host = "north-supply.wholesale.example.test";
        using var updated = await client.SendAsync(fresh, Token);
        updated.EnsureSuccessStatusCode();
        Assert.Equal(
            31,
            (await updated.Content.ReadFromJsonAsync<CatalogResponse>(Token))!
                .Stock
                .AvailableQuantity
        );
    }

    [Fact]
    public async Task PublicAndProtectedReadsCannotFindAnotherOrganizationsSku()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        foreach (
            string path in new[]
            {
                "/organizations/north-supply/catalog?sku=BETA-ONLY",
                "/organizations/north-supply/stock/BETA-ONLY",
            }
        )
        {
            using var request = Request(app, path, "https://identity.test");
            using var absent = await client.SendAsync(request, Token);
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            Assert.Contains("Stock item not found.", await absent.Content.ReadAsStringAsync(Token));
        }
        using var publicRead = await client.GetAsync(
            "/organizations/south-supply/catalog?sku=BETA-ONLY",
            Token
        );
        publicRead.EnsureSuccessStatusCode();
        Assert.Equal(
            new StockAvailability("BETA-ONLY", 13),
            (await publicRead.Content.ReadFromJsonAsync<CatalogResponse>(Token))!.Stock
        );
        using var member = Request(
            app,
            "/organizations/south-supply/stock/BETA-ONLY",
            "https://identity.test"
        );
        using var admitted = await client.SendAsync(member, Token);
        admitted.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task OverlappingRequestsKeepTheirTenantWhileUsingTheSameEfModel()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(async index =>
                {
                    bool alpha = index % 2 == 0;
                    string slug = alpha ? "north-supply" : "south-supply";
                    using var request = Request(
                        app,
                        $"/organizations/{slug}/stock/DEMO-NOTEBOOK",
                        "https://identity.test"
                    );
                    using var response = await client.SendAsync(request, Token);
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadFromJsonAsync<CatalogResponse>(Token);
                    Assert.Equal(
                        alpha ? "wholesale-alpha" : "wholesale-beta",
                        result!.Context.TenantId
                    );
                    Assert.Equal(alpha ? 42 : 7, result.Stock.AvailableQuantity);
                })
        );
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RevokedStockAdmissionDeniesBeforeUnavailableInventoryIsQueried(int status)
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = Request(
            app,
            "/organizations/north-supply/stock/DEMO-NOTEBOOK",
            "https://identity.test"
        );
        using var before = await client.SendAsync(request, Token);
        before.EnsureSuccessStatusCode();
        string cookie = request.Headers.GetValues("Cookie").Single();
        await ExecuteAsync(
            app,
            $"UPDATE access.memberships SET status = {status} WHERE user_id = 'application-alpha' AND organization_id = 'wholesale-alpha'; ALTER TABLE inventory.stock_availability RENAME TO unavailable_stock"
        );
        using var denied = new HttpRequestMessage(
            HttpMethod.Get,
            "/organizations/north-supply/stock/DEMO-NOTEBOOK"
        );
        denied.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(denied, Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(
            "Organization selection failed.",
            await response.Content.ReadAsStringAsync(Token)
        );
        using var anonymous = await client.GetAsync(
            "/organizations/north-supply/stock/DEMO-NOTEBOOK",
            Token
        );
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task PublicNonMemberSeesIsolatedRowsButCannotReadProtectedStock()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var publicRequest = Request(
            app,
            "/organizations/north-supply/catalog",
            "https://other-identity.test"
        );
        using var publicResponse = await client.SendAsync(publicRequest, Token);
        publicResponse.EnsureSuccessStatusCode();
        var result = await publicResponse.Content.ReadFromJsonAsync<CatalogResponse>(Token);
        Assert.Equal("application-beta", result!.Context.ActorId);
        Assert.Equal("wholesale-alpha", result.Context.TenantId);
        Assert.Equal(42, result.Stock.AvailableQuantity);
        using var denied = Request(
            app,
            "/organizations/north-supply/stock/DEMO-NOTEBOOK",
            "https://other-identity.test"
        );
        using var protectedResponse = await client.SendAsync(denied, Token);
        Assert.Equal(HttpStatusCode.NotFound, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task InventoryFaultIsNotADataFallbackAndFreshReadRecovers()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        await ExecuteAsync(
            app,
            "ALTER TABLE inventory.stock_availability RENAME TO unavailable_stock"
        );
        using var failure = await client.GetAsync("/organizations/north-supply/catalog", Token);
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        await ExecuteAsync(
            app,
            "ALTER TABLE inventory.unavailable_stock RENAME TO stock_availability"
        );
        var recovered = await client.GetFromJsonAsync<CatalogResponse>(
            "/organizations/north-supply/catalog",
            Token
        );
        Assert.Equal(42, recovered!.Stock.AvailableQuantity);
    }

    [Fact]
    public async Task CancelledBlockedCatalogReadProducesNoResultAndFreshReadRecovers()
    {
        var finished = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await using var app = await StartAsync(beforeContext: app =>
            ObservePublication(app, value => finished.TrySetResult(value))
        );
        await using var blocker = new NpgsqlConnection(
            app.Configuration.GetConnectionString("Access")
        );
        await blocker.OpenAsync(Token);
        await using var transaction = await blocker.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "LOCK TABLE inventory.stock_availability IN ACCESS EXCLUSIVE MODE",
                blocker,
                transaction
            )
        )
            await command.ExecuteNonQueryAsync(Token);
        using var client = app.GetTestClient();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task<HttpResponseMessage> operation = client.GetAsync(
            "/organizations/north-supply/catalog",
            cancellation.Token
        );
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await using var observer = new NpgsqlConnection(
                app.Configuration.GetConnectionString("Access")
            );
            await observer.OpenAsync(deadline.Token);
            await using var command = new NpgsqlCommand(
                """
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                    WHERE datname = current_database() AND pid <> pg_backend_pid()
                    AND state = 'active' AND wait_event_type = 'Lock' AND query LIKE '%stock_availability%')
                """,
                observer
            );
            while (!Equals(true, await command.ExecuteScalarAsync(deadline.Token)))
                await Task.Delay(25, deadline.Token);
        }
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), Token));
        await transaction.RollbackAsync(Token);
        var recovered = await client.GetFromJsonAsync<CatalogResponse>(
            "/organizations/north-supply/catalog",
            Token
        );
        Assert.Equal(42, recovered!.Stock.AvailableQuantity);
    }

    [Fact]
    public async Task RelocatedAccessRecognizesCheckpointDatabaseAndInventoryDoesNotChangeItsData()
    {
        await using var app = await StartAsync(initialize: false);
        string checkpointSql = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Access-20a02be.sql"),
            Token
        );
        await ExecuteAsync(app, checkpointSql.TrimStart('\uFEFF'));
        await ExecuteAsync(
            app,
            """
            INSERT INTO access.users (id) VALUES ('existing-user');
            INSERT INTO access.external_identities (issuer, subject, user_id) VALUES ('https://existing.test', 'old-subject', 'existing-user');
            INSERT INTO access.organizations (id, slug) VALUES ('existing-organization', 'existing-org');
            INSERT INTO access.memberships (id, user_id, organization_id, status) VALUES (gen_random_uuid(), 'existing-user', 'existing-organization', 1);
            """
        );
        await using var scope = app.Services.CreateAsyncScope();
        var accessDb = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        Assert.Equal(
            ["20261005204257_InitialAccess"],
            await accessDb.Database.GetAppliedMigrationsAsync(Token)
        );
        Assert.Equal(
            ["20261015212953_MembershipAdministration"],
            await accessDb.Database.GetPendingMigrationsAsync(Token)
        );
        Assert.False(accessDb.Database.HasPendingModelChanges());
        await accessDb.Database.MigrateAsync(Token);
        string[] accessHistory = (
            await accessDb.Database.GetAppliedMigrationsAsync(Token)
        ).ToArray();
        Assert.Equal(accessDb.Database.GetMigrations(), accessHistory);
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await inventoryDb.Database.MigrateAsync(Token);
        Assert.Equal(
            inventoryDb.Database.GetMigrations(),
            await inventoryDb.Database.GetAppliedMigrationsAsync(Token)
        );
        Assert.False(inventoryDb.Database.HasPendingModelChanges());
        var access = scope.ServiceProvider.GetRequiredService<IApplicationAccess>();
        Assert.Equal(
            new UserId("existing-user"),
            await access.ResolveUserAsync(
                new ExternalIdentity("https://existing.test", "old-subject"),
                Token
            )
        );
        Assert.Equal(
            new OrganizationId("existing-organization"),
            await access.ResolveMemberOrganizationAsync(
                new UserId("existing-user"),
                "existing-org",
                Token
            )
        );
        Assert.Equal(accessHistory, await accessDb.Database.GetAppliedMigrationsAsync(Token));
    }
}
