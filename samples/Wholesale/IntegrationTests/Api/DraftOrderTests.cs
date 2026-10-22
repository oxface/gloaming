using System.Net;
using System.Net.Http.Json;
using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Access.Contracts;
using ModulithFoundry.Samples.Wholesale.Api;
using ModulithFoundry.Samples.Wholesale.Api.Endpoints;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    private static string DraftPath(string organization = "north-supply") =>
        $"/organizations/{organization}/draft-orders/{DraftOrderAccessDemoSeed.OrderId:D}";

    [Fact]
    public async Task MembershipAdministrationGrantDoesNotGrantPrivateDraftAccess()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        using var client = SecureClient(app);
        using var request = Request(app, DraftPath(), AdministratorIssuer);
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdmittedOperationKeepsItsContextWhileFreshAdmissionStopsAfterSuspension()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        await using var operation = app.Services.CreateAsyncScope();
        var access = operation.ServiceProvider.GetRequiredService<IApplicationAccess>();
        var user = await access.ResolveUserAsync(
            new("https://identity.test", "shared-subject"),
            Token
        );
        operation
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new(Actor.Human(new(user!.Value))));
        var organization = await access.ResolveMemberOrganizationAsync(user, "north-supply", Token);
        Assert.NotNull(organization);
        operation
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new(organization.Value)));
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, true, 1));
        var draft = await operation
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadAsync(DraftOrderAccessDemoSeed.OrderId, Token);
        Assert.Equal(40, draft!.Total);
        Assert.Equal(
            "wholesale-alpha",
            operation
                .ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .Current.RequireTenant()
                .Value
        );
        using var client = SecureClient(app);
        using var next = Request(app, DraftPath(), "https://identity.test");
        using var denied = await client.SendAsync(next, Token);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task ActiveMembershipDoesNotGrantEveryDraftAndTenantCollidingIdsStayPrivate()
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        using (var own = Request(app, DraftPath(), "https://identity.test"))
        using (var response = await client.SendAsync(own, Token))
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal(40, (await response.Content.ReadFromJsonAsync<DraftOrder>(Token))!.Total);
        }

        // Alpha is an active South member, but the identically keyed South draft belongs to Beta.
        using (var foreign = Request(app, DraftPath("south-supply"), "https://identity.test"))
        using (var response = await client.SendAsync(foreign, Token))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using (
            var list = Request(
                app,
                "/organizations/south-supply/draft-orders?limit=1",
                "https://identity.test"
            )
        )
        using (var response = await client.SendAsync(list, Token))
        {
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<DraftOrderPage>(Token);
            Assert.Equal(0, page!.TotalCount);
            Assert.Empty(page.Items);
        }

        using var beta = Request(app, DraftPath("south-supply"), "https://other-identity.test");
        using var betaResponse = await client.SendAsync(beta, Token);
        betaResponse.EnsureSuccessStatusCode();
        Assert.Equal(70, (await betaResponse.Content.ReadFromJsonAsync<DraftOrder>(Token))!.Total);
    }

    [Fact]
    public async Task LateHttpReadAndEditChecksUseTheSameModuleGrantsAsDirectCalls()
    {
        await using var app = await StartAsync();
        await ExecuteAsync(
            app,
            """
            INSERT INTO access.memberships (id, organization_id, user_id, status)
            VALUES (gen_random_uuid(), 'wholesale-alpha', 'application-beta', 1);
            INSERT INTO sales.draft_order_grants (organization_key, order_id, subject_kind, subject_key, permission)
            VALUES ('wholesale-alpha', @order, 2, 'application-beta', 1);
            """,
            new NpgsqlParameter("order", DraftOrderAccessDemoSeed.OrderId)
        );
        using var client = SecureClient(app);
        using (var read = Request(app, DraftPath(), "https://other-identity.test"))
        using (var response = await client.SendAsync(read, Token))
            response.EnsureSuccessStatusCode();

        var reader = await IssueTokenAsync(app, client, "https://other-identity.test");
        using (var change = DraftEditRequest(reader))
        using (var response = await client.SendAsync(change, Token))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using (var direct = AdministrationScope(app, "application-beta"))
        {
            var database = direct.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<DraftOrderChangeResult.NotFound>(
                await direct
                    .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                    .ChangeLinesAsync(
                        new(
                            DraftOrderAccessDemoSeed.OrderId,
                            1,
                            [
                                new DraftOrderLineChange.Change(
                                    DraftOrderAccessDemoSeed.LineId,
                                    2,
                                    25
                                ),
                            ]
                        ),
                        Token
                    )
            );
            Assert.False(database.ChangeTracker.HasChanges());
        }

        var editor = await IssueTokenAsync(app, client);
        using (var change = DraftEditRequest(editor))
        using (var response = await client.SendAsync(change, Token))
        {
            response.EnsureSuccessStatusCode();
            var draft = await response.Content.ReadFromJsonAsync<DraftOrder>(Token);
            Assert.Equal(2, draft!.Version);
            Assert.Equal(50, draft.Total);
        }

        using var stale = DraftEditRequest(editor);
        using var conflict = await client.SendAsync(stale, Token);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task ResourceGrantDoesNotBypassFreshMembershipAdmissionAndReactivationRetainsIt()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        using var client = SecureClient(app);
        var session = await IssueTokenAsync(app, client);
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, true, 1));
        using (var read = Request(app, DraftPath(), "https://identity.test"))
        using (var denied = await client.SendAsync(read, Token))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using (var edit = DraftEditRequest(session))
        using (var denied = await client.SendAsync(edit, Token))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, false, 2));
        using var current = Request(app, DraftPath(), "https://identity.test");
        using var response = await client.SendAsync(current, Token);
        response.EnsureSuccessStatusCode();
        var unchanged = await response.Content.ReadFromJsonAsync<DraftOrder>(Token);
        Assert.Equal(1, unchanged!.Version);
        Assert.Equal(40, unchanged.Total);
    }

    [Fact]
    public async Task NewHumanWorkerOperationAdmitsBeforeUsingRetainedResourceGrants()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, true, 1));
        await using var operation = app.Services.CreateAsyncScope();
        var access = operation.ServiceProvider.GetRequiredService<IApplicationAccess>();
        Assert.Null(
            await access.ResolveMemberOrganizationAsync(
                new("application-alpha"),
                "north-supply",
                Token
            )
        );
        // An honest host stops before tenancy/resource work; copied IDs alone confer no trust.
        Assert.Throws<InvalidOperationException>(() =>
            operation
                .ServiceProvider.GetRequiredService<Gloaming.Tenancy.ITenantContextAccessor>()
                .Current
        );
    }

    [Theory]
    [InlineData("/organizations/north-supply/draft-orders")]
    [InlineData("/organizations/north-supply/draft-orders/a2000000-0000-0000-0000-000000000001")]
    public async Task PrivateDraftHttpKeepsNativeChallenge(string path)
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        using var response = await client.GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PrivateDraftMutationRequiresAntiforgeryAndRejectsInvalidQueryInput()
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var session = await IssueTokenAsync(app, client);
        using (var edit = DraftEditRequest(session))
        {
            edit.Headers.Remove("X-CSRF-TOKEN");
            using var denied = await client.SendAsync(edit, Token);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }

        using var list = Request(
            app,
            "/organizations/north-supply/draft-orders?limit=101",
            "https://identity.test"
        );
        using var invalid = await client.SendAsync(list, Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static HttpRequestMessage DraftEditRequest(ProtectedSession session)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, DraftPath() + "/line")
        {
            Content = JsonContent.Create(
                new DraftOrderLineEdit(1, DraftOrderAccessDemoSeed.LineId, 2, 25)
            ),
        };
        request.Headers.Add(
            "Cookie",
            session.AuthenticationCookie + "; " + session.AntiforgeryCookie
        );
        request.Headers.Add("X-CSRF-TOKEN", session.RequestToken);
        return request;
    }
}
