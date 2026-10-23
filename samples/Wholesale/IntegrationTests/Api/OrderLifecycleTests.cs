using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Api.Endpoints;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    [Fact]
    public async Task ProductApiCreatesChangesAndSubmitsWithRequiredAllowanceRollback()
    {
        await using var app = await StartAsync();
        await using (var setup = AdministrationScope(app, "application-alpha"))
        {
            var database = setup.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            SalesDemoSeed.StageOrderAllowance(database, 50);
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        using var client = SecureClient(app);
        var session = await IssueTokenAsync(app, client);
        var id = Guid.NewGuid();
        var line = Guid.NewGuid();
        var added = Guid.NewGuid();
        var path = "/organizations/north-supply/draft-orders";
        var create = new CreateDraftOrder(
            id,
            SalesDemoSeed.AlphaCustomerId,
            "PRODUCT-ORDER",
            "EUR",
            100,
            [new(line, "DEMO-NOTEBOOK", 2, 20)]
        );
        using (var unprotected = Request(app, path, "https://identity.test"))
        {
            unprotected.Method = HttpMethod.Post;
            unprotected.Content = JsonContent.Create(create);
            using var rejected = await client.SendAsync(unprotected, Token);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using (var request = ProductOrderRequest(session, HttpMethod.Post, path, create))
        using (var response = await client.SendAsync(request, Token))
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal(1, (await response.Content.ReadFromJsonAsync<DraftOrder>(Token))!.Version);
        }

        path += "/" + id.ToString("D");
        using (
            var request = ProductOrderRequest(
                session,
                HttpMethod.Put,
                path + "/lines",
                new DraftLinesEdit(1, [new(added, "DEMO-NOTEBOOK", 1, 20)], [], [])
            )
        )
        using (var response = await client.SendAsync(request, Token))
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal(60, (await response.Content.ReadFromJsonAsync<DraftOrder>(Token))!.Total);
        }

        using (
            var request = ProductOrderRequest(
                session,
                HttpMethod.Post,
                path + "/submit",
                new SubmitDraft(2)
            )
        )
        using (var response = await client.SendAsync(request, Token))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using (var read = Request(app, path, "https://identity.test"))
        using (var response = await client.SendAsync(read, Token))
        {
            var unchanged = await response.Content.ReadFromJsonAsync<DraftOrder>(Token);
            Assert.False(unchanged!.IsSubmitted);
            Assert.Equal(2, unchanged.Version);
        }

        using (
            var request = ProductOrderRequest(
                session,
                HttpMethod.Put,
                path + "/lines",
                new DraftLinesEdit(2, [], [], [added])
            )
        )
        using (var response = await client.SendAsync(request, Token))
            response.EnsureSuccessStatusCode();

        Guid submissionId = Guid.NewGuid();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using (
                var request = ProductOrderRequest(
                    session,
                    HttpMethod.Post,
                    path + "/submit",
                    new SubmitDraft(3, submissionId)
                )
            )
            using (var response = await client.SendAsync(request, Token))
            {
                response.EnsureSuccessStatusCode();
                var submitted = await response.Content.ReadFromJsonAsync<DraftOrder>(Token);
                Assert.True(submitted!.IsSubmitted);
                Assert.Equal(4, submitted.Version);
            }
        }

        using (
            var request = ProductOrderRequest(
                session,
                HttpMethod.Post,
                path + "/submit",
                new SubmitDraft(4, submissionId)
            )
        )
        using (var response = await client.SendAsync(request, Token))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var observer = AdministrationScope(app, "application-alpha");
        var databaseAfter = observer.ServiceProvider.GetRequiredService<SalesDbContext>();
        Assert.Equal(
            40,
            await databaseAfter
                .Database.SqlQueryRaw<decimal>(
                    "SELECT reserved AS \"Value\" FROM sales.customer_order_allowances WHERE organization_key = 'wholesale-alpha'"
                )
                .SingleAsync(Token)
        );
    }

    [Fact]
    public async Task RestrictedSalesHttpModeRetainsItsExplicitExistingDraftScope()
    {
        await using var proof = await StartRowSecurityAsync();
        var endpoints = proof
            .App.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .ToArray();
        Assert.Contains(
            endpoints,
            endpoint =>
                endpoint.RoutePattern.RawText == "/organizations/{organization}/draft-orders"
        );
        Assert.DoesNotContain(
            endpoints,
            endpoint =>
                endpoint.RoutePattern.RawText!.StartsWith(
                    "/organizations/{organization}/draft-orders",
                    StringComparison.Ordinal
                )
                && endpoint
                    .Metadata.GetMetadata<HttpMethodMetadata>()!
                    .HttpMethods.Contains("POST", StringComparer.Ordinal)
        );
        var before = await proof.ScalarAsync<long>("SELECT count(*) FROM sales.draft_orders");
        using var client = SecureClient(proof.App);
        var session = await IssueTokenAsync(proof.App, client);
        using var request = ProductOrderRequest(
            session,
            HttpMethod.Post,
            "/organizations/north-supply/draft-orders",
            new { }
        );
        using var response = await client.SendAsync(request, Token);
        // With no matched organization endpoint the native tenant middleware rejects this generated route.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            before,
            await proof.ScalarAsync<long>("SELECT count(*) FROM sales.draft_orders")
        );
    }

    private static HttpRequestMessage ProductOrderRequest<T>(
        ProtectedSession session,
        HttpMethod method,
        string path,
        T body
    )
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(
            "Cookie",
            session.AuthenticationCookie + "; " + session.AntiforgeryCookie
        );
        request.Headers.Add("X-CSRF-TOKEN", session.RequestToken);
        return request;
    }
}
