using System.Net;
using System.Net.Http.Json;
using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Access.Contracts;
using ModulithFoundry.Samples.Wholesale.Api;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    [Fact]
    public async Task SalesHttpRlsUsesAdmittedTenantAcrossPooledRequestsAndKeepsResourcePermissions()
    {
        await using var proof = await StartRowSecurityAsync(app =>
            app.MapGet("/organizations/{organization}/rls-probe", ProbeRowSecurityAsync)
                .RequireAuthorization()
        );
        await proof.SqlAsync(
            """
            INSERT INTO sales.customer_profiles (id, organization_key, code, display_name, version)
            VALUES (gen_random_uuid(), 'wholesale-alpha ', 'TRAILING', 'Must stay hidden', 1),
                   (gen_random_uuid(), 'WHOLESALE-ALPHA', 'UPPER', 'Must stay hidden too', 1);
            """
        );
        using var client = SecureClient(proof.App);
        int? backend = null;
        foreach (
            var (slug, tenant, name) in new[]
            {
                ("north-supply", "wholesale-alpha", "Alpha Customer"),
                ("south-supply", "wholesale-beta", "Beta Customer"),
                ("north-supply", "wholesale-alpha", "Alpha Customer"),
            }
        )
        {
            using var request = Request(
                proof.App,
                $"/organizations/{slug}/rls-probe",
                "https://identity.test"
            );
            using var response = await client.SendAsync(request, Token);
            response.EnsureSuccessStatusCode();
            var probe = (await response.Content.ReadFromJsonAsync<RowSecurityProbe>(Token))!;
            Assert.Equal(proof.SalesRole, probe.Role);
            Assert.Equal(tenant, probe.Tenant);
            Assert.Equal(name, probe.Profiles);
            Assert.Equal(1, probe.Drafts);
            backend ??= probe.Backend;
            Assert.Equal(backend, probe.Backend);
        }

        Assert.Equal("Alpha Customer", (await ReadProfileAsync(proof.App, client)).DisplayName);
        using (var own = Request(proof.App, DraftPath(), "https://identity.test"))
        using (var response = await client.SendAsync(own, Token))
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal(40, (await response.Content.ReadFromJsonAsync<DraftOrder>(Token))!.Total);
        }

        using (
            var ownList = Request(
                proof.App,
                "/organizations/north-supply/draft-orders?limit=1",
                "https://identity.test"
            )
        )
        using (var response = await client.SendAsync(ownList, Token))
        {
            response.EnsureSuccessStatusCode();
            var ownPage = (await response.Content.ReadFromJsonAsync<DraftOrderPage>(Token))!;
            Assert.Equal(1, ownPage.TotalCount);
            Assert.Equal(40, Assert.Single(ownPage.Items).Total);
        }
        using (
            var foreign = Request(
                proof.App,
                ProfilePath(SalesDemoSeed.BetaCustomerId),
                "https://identity.test"
            )
        )
        using (var response = await client.SendAsync(foreign, Token))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using (
            var privateDraft = Request(
                proof.App,
                DraftPath("south-supply"),
                "https://identity.test"
            )
        )
        using (var response = await client.SendAsync(privateDraft, Token))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var list = Request(
            proof.App,
            "/organizations/south-supply/draft-orders?limit=1",
            "https://identity.test"
        );
        using var listResponse = await client.SendAsync(list, Token);
        listResponse.EnsureSuccessStatusCode();
        var page = (await listResponse.Content.ReadFromJsonAsync<DraftOrderPage>(Token))!;
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
        await using var idle = new NpgsqlConnection(proof.SalesConnection);
        await idle.OpenAsync(Token);
        await using var baseline = new NpgsqlCommand(
            "SELECT NULLIF(current_setting('app.tenant_key', true), '') IS NULL",
            idle
        );
        Assert.Equal(true, await baseline.ExecuteScalarAsync(Token));
    }

    [Fact]
    public async Task SalesHttpRlsCommitsProfileAuditAndDraftEditWithoutExplicitTenantSetup()
    {
        await using var proof = await StartRowSecurityAsync();
        using var client = SecureClient(proof.App);
        var session = await IssueTokenAsync(proof.App, client);
        using (var profile = EditRequest(session))
        using (var response = await client.SendAsync(profile, Token))
            response.EnsureSuccessStatusCode();

        using (var draft = DraftEditRequest(session))
        using (var response = await client.SendAsync(draft, Token))
        {
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<DraftOrder>(Token))!;
            Assert.Equal(2, result.Version);
            Assert.Equal(50, result.Total);
        }

        Assert.Equal(2, (await ReadProfileAsync(proof.App, client)).Version);
        Assert.True(
            await proof.ScalarAsync<bool>(
                """
                SELECT (SELECT count(*) FROM sales.audit_entries) = 1
                    AND (SELECT count(*) FROM sales.outbox_messages) = 0
                    AND EXISTS (SELECT 1 FROM sales.audit_entries WHERE tenant_key = 'wholesale-alpha'
                        AND actor_kind = 2 AND actor_key = 'application-alpha' AND action = 'customer-profile.changed'
                        AND (details->>'PreviousVersion')::bigint = 1 AND (details->>'Version')::bigint = 2)
                    AND EXISTS (SELECT 1 FROM sales.customer_profiles WHERE organization_key = 'wholesale-beta'
                        AND version = 1 AND display_name = 'Beta Customer')
                    AND EXISTS (SELECT 1 FROM sales.draft_orders WHERE organization_key = 'wholesale-beta' AND version = 1)
                """
            )
        );

        await using var runtime = new NpgsqlConnection(proof.SalesConnection);
        await runtime.OpenAsync(Token);
        await using var publication = new NpgsqlCommand(
            """
            INSERT INTO sales.outbox_messages
                (message_id, destination, message_name, schema_version, payload, owner_key, attempts)
            VALUES (gen_random_uuid(), 'unselected.destination', 'unselected.message', 1,
                '{}', 'wholesale-alpha', 0)
            """,
            runtime
        );
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            publication.ExecuteNonQueryAsync(Token)
        );
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }

    [Fact]
    public async Task SalesHttpRlsAuditFailureRollsBackProfileAndFreshRequestRecovers()
    {
        await using var proof = await StartRowSecurityAsync();
        using var client = SecureClient(proof.App);
        var before = await ReadProfileAsync(proof.App, client);
        var session = await IssueTokenAsync(proof.App, client);
        await proof.SqlAsync(
            "ALTER TABLE sales.audit_entries ADD CONSTRAINT proof_audit CHECK (schema_version > 1)"
        );
        using (var edit = EditRequest(session))
        using (var failed = await client.SendAsync(edit, Token))
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        Assert.Equal(before, await ReadProfileAsync(proof.App, client));
        Assert.Equal(0, await proof.ScalarAsync<long>("SELECT count(*) FROM sales.audit_entries"));
        await proof.SqlAsync("ALTER TABLE sales.audit_entries DROP CONSTRAINT proof_audit");
        using var retry = EditRequest(session);
        using var recovered = await client.SendAsync(retry, Token);
        recovered.EnsureSuccessStatusCode();
        Assert.Equal(2, (await ReadProfileAsync(proof.App, client)).Version);
    }

    [Theory]
    [InlineData("owner-update")]
    [InlineData("foreign-audit")]
    [InlineData("grant-administration")]
    public async Task SalesHttpRlsRejectsRawWritesOutsideItsTenantOrTablePrivileges(string path)
    {
        await using var proof = await StartRowSecurityAsync();
        await using var scope = proof.App.Services.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new("wholesale-alpha")));
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        string sql = path switch
        {
            "owner-update" =>
                "UPDATE sales.customer_profiles SET organization_key = 'wholesale-beta' WHERE organization_key = 'wholesale-alpha'",
            "foreign-audit" => """
                INSERT INTO sales.audit_entries (id, occurred_at, actor_kind, source, action, subject_type, outcome, schema_version, details, tenant_key)
                VALUES (gen_random_uuid(), now(), 2, 'sales', 'proof', 'customer', 'accepted', 1, jsonb_build_object(), 'wholesale-beta')
                """,
            _ => "DELETE FROM sales.draft_order_grants WHERE organization_key = 'wholesale-alpha'",
        };
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            database.Database.ExecuteSqlRawAsync(sql, Token)
        );
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    [Fact]
    public async Task SalesHttpRlsHidesForeignWriteTargetsAndRequiresTheCallerTransaction()
    {
        await using var proof = await StartRowSecurityAsync();
        await using var scope = proof.App.Services.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new("wholesale-alpha")));
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new(Actor.Human(new("application-alpha"))));
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var queries = scope.ServiceProvider.GetRequiredService<IDraftOrderQueries>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queries.ReadAsync(DraftOrderAccessDemoSeed.OrderId, Token)
        );
        await using (var transaction = await database.Database.BeginTransactionAsync(Token))
        {
            Assert.Equal(
                40,
                (await queries.ReadAsync(DraftOrderAccessDemoSeed.OrderId, Token))!.Total
            );
            Assert.Equal(
                0,
                await database.Database.ExecuteSqlRawAsync(
                    "UPDATE sales.customer_profiles SET display_name = 'leaked' WHERE organization_key = 'wholesale-beta'",
                    Token
                )
            );
            Assert.Equal(
                0,
                await database.Database.ExecuteSqlRawAsync(
                    "DELETE FROM sales.customer_profiles WHERE organization_key = 'wholesale-beta'",
                    Token
                )
            );
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queries.ReadAsync(DraftOrderAccessDemoSeed.OrderId, Token)
        );
        await using var missing = proof.App.Services.CreateAsyncScope();
        var missingDatabase = missing.ServiceProvider.GetRequiredService<SalesDbContext>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            missingDatabase.Database.BeginTransactionAsync(Token)
        );
        missing
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.Tenantless());
        await Assert.ThrowsAsync<TenantRequiredException>(() =>
            missingDatabase.Database.BeginTransactionAsync(Token)
        );
        Assert.Null(missingDatabase.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("disabled-policy")]
    [InlineData("extra-policy")]
    [InlineData("transport-grant")]
    [InlineData("outbox-grant")]
    [InlineData("missing-audit-insert")]
    public async Task SalesHttpRlsCatalogCheckRejectsMissingPoliciesOrBroaderPrivileges(
        string fault
    )
    {
        await using var proof = await StartRowSecurityAsync();
        await proof.SqlAsync(
            fault switch
            {
                "disabled-policy" =>
                    "ALTER TABLE sales.customer_profiles DISABLE ROW LEVEL SECURITY",
                "extra-policy" => "CREATE POLICY unsafe ON sales.customer_profiles USING (true)",
                "missing-audit-insert" =>
                    $"REVOKE INSERT ON sales.audit_entries FROM \"{proof.SalesRole}\"",
                "outbox-grant" => $"GRANT INSERT ON sales.outbox_messages TO \"{proof.SalesRole}\"",
                _ => $"GRANT SELECT ON sales.inbox_messages TO \"{proof.SalesRole}\"",
            }
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SalesHttpRowSecurity.VerifyRuntimeAsync(
                proof.SalesConnection,
                proof.App.Configuration.GetConnectionString("Access")!,
                Token
            )
        );
    }

    [Fact]
    public async Task SalesHttpRlsRejectsAHostThatRetainsPrivilegedAccessCredentials()
    {
        await using var proof = await StartRowSecurityAsync();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            proof.VerifyPrivilegedAccessAsync
        );
        Assert.Contains("Access/Inventory credential", failure.Message, StringComparison.Ordinal);
        using var client = SecureClient(proof.App);
        using var challenge = await client.GetAsync(DraftPath(), Token);
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        using var catalog = await client.GetAsync("/organizations/north-supply/catalog", Token);
        catalog.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SalesHttpRlsPreservesAdmittedWorkAndFreshMembershipAdmission()
    {
        await using var proof = await StartRowSecurityAsync();
        await PrepareMembershipsAsync(proof.App);
        await using var operation = proof.App.Services.CreateAsyncScope();
        var access = operation.ServiceProvider.GetRequiredService<IApplicationAccess>();
        var user = await access.ResolveUserAsync(
            new("https://identity.test", "shared-subject"),
            Token
        );
        Assert.NotNull(await access.ResolveMemberOrganizationAsync(user!, "north-supply", Token));
        operation
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new(Actor.Human(new(user!.Value))));
        operation
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new("wholesale-alpha")));
        Assert.IsType<MembershipChangeResult.Changed>(
            await ChangeMembershipAsync(proof.App, true, 1)
        );
        var database = operation.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using (var transaction = await database.Database.BeginTransactionAsync(Token))
        {
            var draft = await operation
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .ReadAsync(DraftOrderAccessDemoSeed.OrderId, Token);
            Assert.Equal(40, draft!.Total);
        }

        using var client = SecureClient(proof.App);
        using var next = Request(proof.App, DraftPath(), "https://identity.test");
        using var denied = await client.SendAsync(next, Token);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(
            "wholesale-alpha",
            operation
                .ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .Current.RequireTenant()
                .Value
        );
    }

    [Fact]
    public async Task SalesHttpRlsFiniteSetupCommandInstallsPoliciesWithoutStartingHttp()
    {
        await using var proof = await StartRowSecurityAsync(commandSetup: true);
        using var client = SecureClient(proof.App);
        Assert.Equal("Alpha Customer", (await ReadProfileAsync(proof.App, client)).DisplayName);
    }

    private sealed record RowSecurityProbe(
        string Role,
        string Tenant,
        int Backend,
        string Profiles,
        int Drafts
    );

    private static async Task<RowSecurityProbe> ProbeRowSecurityAsync(
        SalesDbContext database,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        // Intentionally omit every tenant predicate; PostgreSQL must supply the boundary.
        return await database
            .Database.SqlQueryRaw<RowSecurityProbe>(
                """
                SELECT current_user::text AS "Role", current_setting('app.tenant_key') AS "Tenant",
                    pg_backend_pid() AS "Backend",
                    (SELECT string_agg(display_name, ',' ORDER BY display_name) FROM sales.customer_profiles) AS "Profiles",
                    (SELECT count(*)::int FROM sales.draft_orders) AS "Drafts"
                """
            )
            .SingleAsync(cancellationToken);
    }
}
