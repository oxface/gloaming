using System.Net;
using System.Net.Http.Json;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Access.Contracts;
using ModulithFoundry.Samples.Wholesale.Access.Persistence;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints;
using ModulithFoundry.Samples.Wholesale.Sales;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Tests;

public sealed partial class CompositionTests
{
    private static readonly Guid TargetMembership = Guid.Parse(
        "a1100000-0000-0000-0000-000000000001"
    );
    private static readonly Guid AdministratorMembership = Guid.Parse(
        "a1100000-0000-0000-0000-000000000002"
    );
    private const string Administrator = "membership-administrator";
    private const string AdministratorIssuer = "https://membership-administrator.test";
    private const string OrdinaryIssuer = "https://ordinary-member.test";

    [Fact]
    public async Task AdministrativeSuspensionBlocksSameCookieBusinessWorkAndReactivationPreservesIdentity()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app, targetAdministrator: true);
        using var client = SecureClient(app);
        var profile = await ReadProfileAsync(app, client);
        var other = await ReadProfileAsync(
            app,
            client,
            SalesDemoSeed.BetaCustomerId,
            "south-supply"
        );
        var targetSession = await IssueTokenAsync(app, client);
        var administrator = await IssueTokenAsync(app, client, AdministratorIssuer);

        using (var request = MembershipRequest(administrator, suspend: true, revision: 1))
        using (var response = await client.SendAsync(request, Token))
        {
            response.EnsureSuccessStatusCode();
            var changed = await response.Content.ReadFromJsonAsync<OrganizationMembership>(Token);
            Assert.Equal(MembershipStatus.Suspended, changed!.Status);
            Assert.Equal(2, changed.Revision);
            Assert.True(changed.CanAdministerMemberships);
        }

        using (var read = new HttpRequestMessage(HttpMethod.Get, ProfilePath()))
        {
            read.Headers.Add("Cookie", targetSession.AuthenticationCookie);
            using var denied = await client.SendAsync(read, Token);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }

        using (var mutation = EditRequest(targetSession))
        using (var denied = await client.SendAsync(mutation, Token))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using (var identity = new HttpRequestMessage(HttpMethod.Get, "/identity"))
        {
            identity.Headers.Add("Cookie", targetSession.AuthenticationCookie);
            using var response = await client.SendAsync(identity, Token);
            response.EnsureSuccessStatusCode();
            Assert.Equal(
                "application-alpha",
                (await response.Content.ReadFromJsonAsync<IdentityResponse>(Token))!.ActorId
            );
        }

        using (
            var otherRead = new HttpRequestMessage(
                HttpMethod.Get,
                ProfilePath(other.CustomerId, "south-supply")
            )
        )
        {
            otherRead.Headers.Add("Cookie", targetSession.AuthenticationCookie);
            using var response = await client.SendAsync(otherRead, Token);
            response.EnsureSuccessStatusCode();
        }

        using (
            var otherEdit = EditRequest(
                targetSession,
                new ProfileEdit(other.AddressId, 1, "Other Organization", "Other street"),
                ProfilePath(other.CustomerId, "south-supply")
            )
        )
        using (var response = await client.SendAsync(otherEdit, Token))
            response.EnsureSuccessStatusCode();

        // Public Organization routes deliberately remain public after protected admission closes.
        using (
            var catalog = Request(
                app,
                "/organizations/north-supply/catalog",
                "https://identity.test"
            )
        )
        using (var response = await client.SendAsync(catalog, Token))
            response.EnsureSuccessStatusCode();

        await using (var inspect = AdministrationScope(app))
        {
            var sales = inspect.ServiceProvider.GetRequiredService<SalesDbContext>();
            var persisted = await inspect
                .ServiceProvider.GetRequiredService<ModulithFoundry.Samples.Wholesale.Sales.Contracts.ICustomerProfiles>()
                .ReadAsync(profile.CustomerId, Token);
            Assert.Equal(profile, persisted);
            Assert.Empty(await sales.Set<AuditRecord>().AsNoTracking().ToArrayAsync(Token));
            Assert.Equal(
                0,
                await sales
                    .Database.SqlQueryRaw<long>(
                        "SELECT count(*) AS \"Value\" FROM sales.outbox_messages"
                    )
                    .SingleAsync(Token)
            );
        }

        using (var request = MembershipRequest(administrator, suspend: false, revision: 2))
        using (var response = await client.SendAsync(request, Token))
            response.EnsureSuccessStatusCode();

        Assert.Equal(profile, await ReadProfileAsync(app, client));
        // Retained administration authority is eligible again, checked from persisted state.
        using (var selfRead = Request(app, MembershipPath(), "https://identity.test"))
        using (var response = await client.SendAsync(selfRead, Token))
            response.EnsureSuccessStatusCode();

        var state = await StoredMembershipAsync(app);
        Assert.Equal("application-alpha", state.UserId);
        Assert.Equal(1, state.Status);
        Assert.Equal(3, state.Revision);
        var audits = await MembershipAuditsAsync(app);
        Assert.Equal(2, audits.Length);
        Assert.All(
            audits,
            entry =>
            {
                Assert.Equal(ActorKind.Human, entry.ActorKind);
                Assert.Equal(Administrator, entry.ActorKey);
                Assert.Equal("wholesale-alpha", entry.TenantKey);
                Assert.Equal("membership", entry.SubjectType);
                Assert.Equal(TargetMembership.ToString("D"), entry.SubjectKey);
                Assert.Equal("accepted", entry.Outcome);
                Assert.Null(entry.InitiatorKey);
                Assert.Equal("application-alpha", entry.Details.GetProperty("UserId").GetString());
                Assert.Equal(5, entry.Details.EnumerateObject().Count());
            }
        );
        Assert.Contains(
            audits,
            entry =>
                entry.Action == "membership.suspended"
                && entry.Details.GetProperty("PreviousRevision").GetInt64() == 1
                && entry.Details.GetProperty("Revision").GetInt64() == 2
        );
        Assert.Contains(
            audits,
            entry =>
                entry.Action == "membership.reactivated"
                && entry.Details.GetProperty("PreviousStatus").GetInt32() == 2
                && entry.Details.GetProperty("Status").GetInt32() == 1
        );
    }

    [Fact]
    public async Task RepeatConflictAndAbaOutcomesDoNotDuplicateTransitionsOrAudits()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, true, 1));
        Assert.IsType<MembershipChangeResult.Conflict>(await ChangeMembershipAsync(app, true, 1));
        Assert.IsType<MembershipChangeResult.AlreadyInRequestedState>(
            await ChangeMembershipAsync(app, true, 2)
        );
        Assert.Single(await MembershipAuditsAsync(app));
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, false, 2));
        Assert.IsType<MembershipChangeResult.Conflict>(await ChangeMembershipAsync(app, true, 1));
        Assert.IsType<MembershipChangeResult.Conflict>(await ChangeMembershipAsync(app, false, 2));
        Assert.IsType<MembershipChangeResult.AlreadyInRequestedState>(
            await ChangeMembershipAsync(app, false, 3)
        );
        Assert.Equal(3, (await StoredMembershipAsync(app)).Revision);
        Assert.Equal(2, (await MembershipAuditsAsync(app)).Length);

        // A new/repeated request reauthorizes even when its target is already in that state.
        await ExecuteAsync(
            app,
            "UPDATE access.memberships SET can_administer_memberships = false WHERE user_id = 'membership-administrator'"
        );
        Assert.IsType<MembershipChangeResult.NotFound>(await ChangeMembershipAsync(app, false, 3));
        Assert.Equal(2, (await MembershipAuditsAsync(app)).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelfSuspensionAndLastAdministratorLossAreRejected(bool anotherAdministrator)
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app, anotherAdministrator);
        var result = await ChangeMembershipAsync(app, true, 1, AdministratorMembership);
        if (anotherAdministrator)
            Assert.IsType<MembershipChangeResult.SelfSuspensionNotAllowed>(result);
        else
            Assert.IsType<MembershipChangeResult.LastAdministrator>(result);
        Assert.Empty(await MembershipAuditsAsync(app));
        Assert.Equal(1, (await StoredMembershipAsync(app, AdministratorMembership)).Status);
    }

    [Fact]
    public async Task ReactivationDoesNotRecreateAGrantRevokedDuringSuspension()
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app, targetAdministrator: true);
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, true, 1));
        await ExecuteAsync(
            app,
            "UPDATE access.memberships SET can_administer_memberships = false WHERE user_id = 'application-alpha' AND organization_id = 'wholesale-alpha'"
        );
        Assert.IsType<MembershipChangeResult.Changed>(await ChangeMembershipAsync(app, false, 2));
        Assert.False((await StoredMembershipAsync(app)).CanAdminister);
        await using var scope = AdministrationScope(app, "application-alpha");
        Assert.Null(
            await scope
                .ServiceProvider.GetRequiredService<IMembershipAdministration>()
                .ReadAsync(TargetMembership, Token)
        );
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("foreign-target")]
    [InlineData("missing-target")]
    public async Task UnauthorizedAndForeignTargetsHaveTheSameNonDisclosingOutcome(string scenario)
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        using var client = SecureClient(app);
        string issuer = scenario == "ordinary" ? OrdinaryIssuer : AdministratorIssuer;
        var session = await IssueTokenAsync(app, client, issuer);
        Guid target = scenario switch
        {
            "foreign-target" => await MembershipIdAsync(app, "application-alpha", "wholesale-beta"),
            "missing-target" => Guid.NewGuid(),
            _ => TargetMembership,
        };
        using var request = MembershipRequest(session, true, 1, target);
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var read = Request(app, MembershipPath(target), issuer);
        using var readResponse = await client.SendAsync(read, Token);
        Assert.Equal(HttpStatusCode.NotFound, readResponse.StatusCode);
        Assert.Equal(1, (await StoredMembershipAsync(app)).Status);
        Assert.Empty(await MembershipAuditsAsync(app));
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("anonymous")]
    [InlineData("revision-zero")]
    [InlineData("revision-overflow")]
    [InlineData("empty-id")]
    public async Task InvalidAdministrativeHttpRequestsLeaveNoEffects(string scenario)
    {
        await using var app = await StartAsync();
        await PrepareMembershipsAsync(app);
        using var client = SecureClient(app);
        var session = await IssueTokenAsync(app, client, AdministratorIssuer);
        long revision = scenario switch
        {
            "revision-zero" => 0,
            "revision-overflow" => long.MaxValue,
            _ => 1,
        };
        using var request = MembershipRequest(
            session,
            true,
            revision,
            scenario == "empty-id" ? Guid.Empty : TargetMembership
        );
        if (scenario == "csrf")
            request.Headers.Remove("X-CSRF-TOKEN");
        if (scenario == "anonymous")
            request.Headers.Remove("Cookie");
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(
            scenario == "anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest,
            response.StatusCode
        );
        Assert.Equal(1, (await StoredMembershipAsync(app)).Revision);
        Assert.Empty(await MembershipAuditsAsync(app));
    }

    private static async Task PrepareMembershipsAsync(
        WebApplication app,
        bool targetAdministrator = false
    )
    {
        await ExecuteAsync(
            app,
            """
            UPDATE access.memberships SET id = 'a1100000-0000-0000-0000-000000000001',
                can_administer_memberships = @target_admin
            WHERE organization_id = 'wholesale-alpha' AND user_id = 'application-alpha';
            INSERT INTO access.users (id) VALUES ('membership-administrator'), ('ordinary-member');
            INSERT INTO access.external_identities (issuer, subject, user_id) VALUES
                ('https://membership-administrator.test', 'shared-subject', 'membership-administrator'),
                ('https://ordinary-member.test', 'shared-subject', 'ordinary-member');
            INSERT INTO access.memberships (id, organization_id, user_id, status, can_administer_memberships) VALUES
                ('a1100000-0000-0000-0000-000000000002', 'wholesale-alpha', 'membership-administrator', 1, true),
                ('a1100000-0000-0000-0000-000000000003', 'wholesale-alpha', 'ordinary-member', 1, false);
            """,
            new NpgsqlParameter("target_admin", targetAdministrator)
        );
    }

    private static string MembershipPath(Guid? id = null) =>
        $"/organizations/north-supply/memberships/{id ?? TargetMembership}";

    private static HttpRequestMessage MembershipRequest(
        ProtectedSession session,
        bool suspend,
        long revision,
        Guid? id = null
    )
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            MembershipPath(id) + (suspend ? "/suspend" : "/reactivate")
        )
        {
            Content = JsonContent.Create(new MembershipEdit(revision)),
        };
        request.Headers.Add(
            "Cookie",
            session.AuthenticationCookie + "; " + session.AntiforgeryCookie
        );
        request.Headers.Add("X-CSRF-TOKEN", session.RequestToken);
        return request;
    }

    private static AsyncServiceScope AdministrationScope(
        WebApplication app,
        string user = Administrator,
        string organization = "wholesale-alpha"
    )
    {
        var scope = app.Services.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(Actor.Human(new ActorId(user))));
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        return scope;
    }

    private static async Task<MembershipChangeResult> ChangeMembershipAsync(
        WebApplication app,
        bool suspend,
        long revision,
        Guid? target = null,
        string user = Administrator
    )
    {
        await using var scope = AdministrationScope(app, user);
        var commands = scope.ServiceProvider.GetRequiredService<IMembershipAdministration>();
        var change = new MembershipChange(target ?? TargetMembership, revision);
        return suspend
            ? await commands.SuspendAsync(change, Token)
            : await commands.ReactivateAsync(change, Token);
    }

    private static async Task<MembershipSnapshot> StoredMembershipAsync(
        WebApplication app,
        Guid? target = null
    )
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        Guid id = target ?? TargetMembership;
        return await database
            .Database.SqlQuery<MembershipSnapshot>(
                $"""
                SELECT id AS "Id", user_id AS "UserId", status AS "Status", revision AS "Revision",
                    can_administer_memberships AS "CanAdminister" FROM access.memberships WHERE id = {id}
                """
            )
            .SingleAsync(Token);
    }

    private static async Task<Guid> MembershipIdAsync(
        WebApplication app,
        string user,
        string organization
    )
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<AccessDbContext>()
            .Database.SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM access.memberships WHERE user_id = {user} AND organization_id = {organization} AND status IN (1,2)
                """
            )
            .SingleAsync(Token);
    }

    private static async Task<AuditRecord[]> MembershipAuditsAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<AccessDbContext>()
            .Set<AuditRecord>()
            .AsNoTracking()
            .OrderBy(row => row.OccurredAt)
            .ThenBy(row => row.Id)
            .ToArrayAsync(Token);
    }

    private sealed record MembershipSnapshot(
        Guid Id,
        string UserId,
        int Status,
        long Revision,
        bool CanAdminister
    );
}
