using System.Net;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Endpoints;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Tests;

public sealed partial class CompositionTests
{
    [Fact]
    public async Task AuditMigrationPreservesPopulatedPreAuditSalesProfiles()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        using var tenant = new TenantContextAccessor();
        tenant.Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        var options = new DbContextOptionsBuilder<SalesDbContext>();
        SalesDatabase.Configure(options, connection);
        await using var database = new SalesDbContext(options.Options, tenant);
        await database.GetService<IMigrator>().MigrateAsync("20261005223044_InitialSales", Token);
        // Seed the historical schema, not today's model (which now includes delivery listing).
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.customer_profiles (id, organization_key, code, display_name, version)
            VALUES ('10000000-0000-0000-0000-000000000001', 'wholesale-alpha', 'DEMO-CUSTOMER', 'Alpha Customer', 1);
            INSERT INTO sales.customer_addresses (id, customer_id, organization_key, address_line)
            VALUES ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'wholesale-alpha', 'Alpha Street');
            """,
            Token
        );
        await database.Database.MigrateAsync(Token);
        Assert.Equal(
            database.Database.GetMigrations(),
            await database.Database.GetAppliedMigrationsAsync(Token)
        );
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.Empty(await database.Set<AuditRecord>().ToArrayAsync(Token));
        Assert.Equal(
            "Alpha Customer",
            await database
                .Database.SqlQueryRaw<string>(
                    "SELECT display_name AS \"Value\" FROM sales.customer_profiles"
                )
                .SingleAsync(Token)
        );
        Assert.Equal(
            1L,
            await database
                .Database.SqlQueryRaw<long>(
                    "SELECT version AS \"Value\" FROM sales.customer_profiles"
                )
                .SingleAsync(Token)
        );
        Assert.Equal(
            "Alpha Street",
            await database
                .Database.SqlQueryRaw<string>(
                    "SELECT address_line AS \"Value\" FROM sales.customer_addresses"
                )
                .SingleAsync(Token)
        );
    }

    [Fact]
    public async Task AcceptedProfileChangeRetainsTrustedActorTenantAndVersionWithoutPrivateText()
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(app, client);
        var session = await IssueTokenAsync(app, client);
        using var request = EditRequest(session);
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();

        var audit = Assert.Single(await ReadAuditsAsync(app));
        Assert.NotEqual(Guid.Empty, audit.Id);
        Assert.Equal(ActorKind.Human, audit.ActorKind);
        Assert.Equal("application-alpha", audit.ActorKey);
        Assert.Null(audit.InitiatorKind);
        Assert.Equal("wholesale-alpha", audit.TenantKey);
        Assert.Equal("sales", audit.Source);
        Assert.Equal("customer-profile.changed", audit.Action);
        Assert.Equal("customer", audit.SubjectType);
        Assert.Equal(before.CustomerId.ToString("D"), audit.SubjectKey);
        Assert.Equal("accepted", audit.Outcome);
        Assert.Equal(1, audit.SchemaVersion);
        Assert.Null(audit.ReasonCode);
        Assert.Equal(before.AddressId, audit.Details.GetProperty("AddressId").GetGuid());
        Assert.Equal(1, audit.Details.GetProperty("PreviousVersion").GetInt64());
        Assert.Equal(2, audit.Details.GetProperty("Version").GetInt64());
        Assert.Equal(3, audit.Details.EnumerateObject().Count());
        Assert.Empty(await ReadAuditsAsync(app, "wholesale-beta"));
        Assert.Equal(2, (await ReadProfileAsync(app, client)).Version);

        using var stale = EditRequest(session);
        using var conflict = await client.SendAsync(stale, Token);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Single(await ReadAuditsAsync(app));
    }

    [Fact]
    public async Task AuditInsertFaultAfterCustomerSaveRollsBackProfileAndFreshRequestRecovers()
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(app, client);
        var session = await IssueTokenAsync(app, client);
        await ExecuteAsync(
            app,
            "ALTER TABLE sales.audit_entries ADD CONSTRAINT proof_audit CHECK (schema_version > 1)"
        );
        using var request = EditRequest(session);
        using var failed = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(before, await ReadProfileAsync(app, client));
        Assert.Empty(await ReadAuditsAsync(app));
        await ExecuteAsync(app, "ALTER TABLE sales.audit_entries DROP CONSTRAINT proof_audit");
        using var retry = EditRequest(session);
        using var recovered = await client.SendAsync(retry, Token);
        recovered.EnsureSuccessStatusCode();
        Assert.Equal(2, (await ReadProfileAsync(app, client)).Version);
        Assert.Single(await ReadAuditsAsync(app));
    }

    [Fact]
    public async Task CommitRequiresMatchingAuditForEverySelectedProfileVersionTransition()
    {
        await using var app = await StartAsync();
        await ExecuteAsync(
            app,
            """
            CREATE FUNCTION sales.proof_required_profile_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.version > OLD.version AND NOT EXISTS (
                    SELECT 1 FROM sales.audit_entries
                    WHERE tenant_key = NEW.organization_key AND subject_key = NEW.id::text
                      AND source = 'sales' AND action = 'customer-profile.changed'
                      AND outcome = 'accepted' AND actor_kind = 2
                      AND actor_key = 'application-alpha'
                      AND schema_version = 1
                      AND (details->>'PreviousVersion')::bigint = OLD.version
                      AND (details->>'Version')::bigint = NEW.version
                ) THEN RAISE EXCEPTION 'profile committed without expected audit'; END IF;
                RETURN NULL;
            END; $$;
            CREATE CONSTRAINT TRIGGER proof_profile_audit AFTER UPDATE ON sales.customer_profiles
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sales.proof_required_profile_audit();
            """
        );
        // Establish that the oracle really rejects an omitted participant.
        var missingAudit = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            ExecuteAsync(
                app,
                "UPDATE sales.customer_profiles SET version = version + 1 WHERE organization_key = 'wholesale-alpha'"
            )
        );
        Assert.Equal("profile committed without expected audit", missingAudit.MessageText);
        using var client = SecureClient(app);
        var session = await IssueTokenAsync(app, client);
        using var first = EditRequest(session);
        using var firstResponse = await client.SendAsync(first, Token);
        firstResponse.EnsureSuccessStatusCode();
        using var next = EditRequest(
            session,
            new ProfileEdit(SalesDemoSeed.AlphaAddressId, 2, "Next", "Next street")
        );
        using var nextResponse = await client.SendAsync(next, Token);
        nextResponse.EnsureSuccessStatusCode();
        Assert.Equal(3, (await ReadProfileAsync(app, client)).Version);
        var audits = await ReadAuditsAsync(app);
        Assert.Equal(2, audits.Length);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("anonymous")]
    [InlineData("system")]
    public async Task ConsumerHumanRequirementRejectsOtherActorsBeforeMutation(string actor)
    {
        await using var app = await StartAsync();
        await using var scope = app.Services.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        if (actor != "missing")
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(
                    new ActorContext(
                        actor == "anonymous" ? Actor.Anonymous : Actor.System(new ActorId("worker"))
                    )
                );

        var profiles = scope.ServiceProvider.GetRequiredService<ICustomerProfiles>();
        var before = await profiles.ReadAsync(SalesDemoSeed.AlphaCustomerId, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
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
        );
        Assert.Equal(before, await profiles.ReadAsync(SalesDemoSeed.AlphaCustomerId, Token));
        Assert.Empty(await ReadAuditsAsync(app));
    }

    [Fact]
    public async Task CancellationAfterFirstSalesSaveLeavesNeitherAcceptedAuditNorBusinessChange()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var app = await StartAsync(configureHost: builder =>
            builder.Services.AddDbContext<SalesDbContext>(options =>
                options.AddInterceptors(new CancelProfileSave(cancellation))
            )
        );
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(app, client);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(Actor.Human(new ActorId("application-alpha"))));
            var profiles = scope.ServiceProvider.GetRequiredService<ICustomerProfiles>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                profiles.ChangeAsync(
                    new CustomerProfileChange(
                        SalesDemoSeed.AlphaCustomerId,
                        SalesDemoSeed.AlphaAddressId,
                        1,
                        "Change",
                        "Street"
                    ),
                    cancellation.Token
                )
            );
        }

        Assert.Equal(before, await ReadProfileAsync(app, client));
        Assert.Empty(await ReadAuditsAsync(app));
    }

    private sealed class CancelProfileSave(CancellationTokenSource cancellation)
        : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (cancellationToken == cancellation.Token)
                cancellation.Cancel();

            return ValueTask.FromResult(result);
        }
    }

    private static async Task<AuditRecord[]> ReadAuditsAsync(
        WebApplication app,
        string tenant = "wholesale-alpha"
    )
    {
        await using var scope = app.Services.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(tenant)));
        return await scope
            .ServiceProvider.GetRequiredService<SalesDbContext>()
            .Set<AuditRecord>()
            .AsNoTracking()
            .ToArrayAsync(Token);
    }
}
