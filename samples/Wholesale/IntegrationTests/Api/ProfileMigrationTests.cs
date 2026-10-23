using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ModulithFoundry.Samples.Wholesale.Api.Endpoints;
using ModulithFoundry.Samples.Wholesale.Sales;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    [Fact]
    public async Task DirectoryRetirementPreservesProfilesAuditAndPendingMessagesWithoutFurtherPublication()
    {
        await using var app = await StartAsync();
        using var client = SecureClient(app);
        var session = await IssueTokenAsync(app, client);
        using (var edit = EditRequest(session))
        using (var response = await client.SendAsync(edit, Token))
            response.EnsureSuccessStatusCode();

        var before = await ReadProfileAsync(app, client);
        using var tenant = new TenantContextAccessor();
        tenant.Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        var options = new DbContextOptionsBuilder<SalesDbContext>();
        SalesDatabase.Configure(options, app.Configuration["ConnectionStrings:Access"]!);
        await using var database = new SalesDbContext(options.Options, tenant);
        // Recreate the populated pre-retirement schema, including a withdrawn listing and queued work.
        await database
            .GetService<IMigrator>()
            .MigrateAsync("20261022174004_AddCustomerDeliveryListing", Token);
        await database.Database.ExecuteSqlRawAsync(
            """
            UPDATE sales.customer_profiles SET delivery_listed = false;
            INSERT INTO sales.outbox_messages
                (message_id, destination, message_name, schema_version, payload, owner_key, attempts)
            VALUES (gen_random_uuid(), 'dispatch.customer-directory', 'sales.customer-delivery-state', 1,
                jsonb_build_object('retained', true), 'wholesale-alpha', 2),
                (gen_random_uuid(), 'inventory.issue-stock', 'inventory.issue-stock', 1,
                jsonb_build_object('retained', true), 'wholesale-beta', 1);
            """,
            Token
        );
        string pending = await PendingAsync();
        string audit = await AuditAsync();

        await database.Database.MigrateAsync(Token);

        Assert.False(database.Database.HasPendingModelChanges());
        Assert.Empty(await database.Database.GetPendingMigrationsAsync(Token));
        Assert.Equal(before, await ReadProfileAsync(app, client));
        Assert.Equal(audit, await AuditAsync());
        Assert.Equal(pending, await PendingAsync());
        Assert.Equal(
            0,
            await database
                .Database.SqlQueryRaw<int>(
                    """
                    SELECT count(*)::int AS "Value" FROM information_schema.columns
                    WHERE table_schema = 'sales' AND table_name = 'customer_profiles' AND column_name = 'delivery_listed'
                    """
                )
                .SingleAsync(Token)
        );

        using (
            var edit = EditRequest(
                session,
                new ProfileEdit(
                    before.AddressId,
                    before.Version,
                    "After retirement",
                    "Retained street"
                )
            )
        )
        using (var response = await client.SendAsync(edit, Token))
            response.EnsureSuccessStatusCode();

        var after = await ReadProfileAsync(app, client);
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal("After retirement", after.DisplayName);
        Assert.Equal("Retained street", after.AddressLine);
        Assert.Equal(2, (await ReadAuditsAsync(app)).Length);
        Assert.Equal(pending, await PendingAsync());

        Task<string> PendingAsync() =>
            database
                .Database.SqlQueryRaw<string>(
                    "SELECT jsonb_agg(to_jsonb(m) ORDER BY message_id)::text AS \"Value\" FROM sales.outbox_messages m"
                )
                .SingleAsync(Token);

        Task<string> AuditAsync() =>
            database
                .Database.SqlQueryRaw<string>(
                    "SELECT jsonb_agg(to_jsonb(a) ORDER BY id)::text AS \"Value\" FROM sales.audit_entries a"
                )
                .SingleAsync(Token);
    }
}
