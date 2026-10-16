using Gloaming.Tenancy;
using Gloaming.Tenancy.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.CqrsDemo.TenantIsolation;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.CqrsDemo.Tests;

public sealed partial class TenantIsolationTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task ExecutableJourneySeparatesDatabaseIsolationFromResourcePermissions()
    {
        await using var proof = await CreateAsync(initialize: false);
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        await TenantIsolationJourney.RunAsync(
            proof.OwnerConnection,
            proof.RuntimeConnection,
            output,
            Token
        );
        Assert.Equal(
            "wholesale-alpha: database rows=1, Alpha's permitted drafts=1."
                + Environment.NewLine
                + "wholesale-beta: database rows=1, Alpha's permitted drafts=0."
                + Environment.NewLine,
            output.ToString()
        );
    }

    [Fact]
    public async Task ActualRuntimeRoleHasNoOwnerBypassOrSchemaAuthority()
    {
        await using var proof = await CreateAsync();
        await SalesRowSecurity.VerifyRuntimeAsync(proof.Runtime, Token);
        await using var connection = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.Equal(
            proof.RuntimeRole,
            await ValueAsync<string>(connection, null, "SELECT current_user")
        );
        Assert.Equal(
            5,
            await ValueAsync<int>(
                connection,
                null,
                """
                SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'sales' AND c.relrowsecurity AND c.relforcerowsecurity
                """
            )
        );
        using var identifiers = new NpgsqlCommandBuilder();
        await DeniedAsync(() =>
            CommandAsync(
                connection,
                null,
                "SET ROLE " + identifiers.QuoteIdentifier(proof.OwnerRole)
            )
        );
        await DeniedAsync(() =>
            CommandAsync(
                connection,
                null,
                "ALTER TABLE sales.draft_orders DISABLE ROW LEVEL SECURITY"
            )
        );
        await DeniedAsync(() => CommandAsync(connection, null, "TRUNCATE sales.draft_orders"));
        await DeniedAsync(() =>
            CommandAsync(connection, null, "SELECT * FROM sales.outbox_messages")
        );
        await DeniedAsync(() =>
            CommandAsync(connection, null, "DELETE FROM sales.draft_order_grants WHERE false")
        );
    }

    [Fact]
    public async Task NativeSqlAndDisabledEfFiltersRemainTenantIsolatedWhileResourceGrantsStillApply()
    {
        await using var proof = await CreateAsync();
        await using var provider = proof.Provider();
        foreach (string tenant in new[] { "wholesale-alpha", "wholesale-beta" })
        {
            await using var scope = TenantIsolationJourney.CreateScope(
                provider,
                tenant,
                "application-alpha"
            );
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await database.EstablishTenantDatabaseContextAsync(
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>(),
                "app.tenant_key",
                Token
            );
            var raw = await database
                .Database.SqlQueryRaw<string>(
                    "SELECT organization_key AS \"Value\" FROM sales.draft_orders"
                )
                .ToArrayAsync(Token);
            Assert.Equal(new[] { tenant }, raw);
            Assert.Equal(1, await database.DraftOrders.IgnoreQueryFilters().CountAsync(Token));
            Assert.Equal(1, await database.DraftOrderLines.IgnoreQueryFilters().CountAsync(Token));
            Assert.Equal(1, await database.DraftOrderGrants.IgnoreQueryFilters().CountAsync(Token));
            var page = await scope
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .SearchAsync(new(), Token);
            Assert.Equal(tenant == "wholesale-alpha" ? 1 : 0, page.TotalCount);
            Assert.Equal(tenant == "wholesale-alpha" ? 1 : 0, page.Items.Count);
            if (tenant == "wholesale-beta")
                Assert.IsType<DraftOrderChangeResult.NotFound>(
                    await scope
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

            await transaction.CommitAsync(Token);
        }
    }

    [Fact]
    public async Task NativeResourceEditRetainsItsPermissionAndVersionProtocolUnderRls()
    {
        await using var proof = await CreateAsync();
        await using var provider = proof.Provider();
        await using var scope = TenantIsolationJourney.CreateScope(
            provider,
            "wholesale-alpha",
            "application-alpha"
        );
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await database.EstablishTenantDatabaseContextAsync(
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>(),
            "app.tenant_key",
            Token
        );
        var result = await scope
            .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
            .ChangeLinesAsync(
                new(
                    DraftOrderAccessDemoSeed.OrderId,
                    1,
                    [new DraftOrderLineChange.Change(DraftOrderAccessDemoSeed.LineId, 2, 25)]
                ),
                Token
            );
        Assert.Equal(50, Assert.IsType<DraftOrderChangeResult.Changed>(result).Proposed.Total);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("owner-update")]
    [InlineData("upsert")]
    public async Task WithCheckAndUpsertRejectForeignOwnership(string operation)
    {
        await using var proof = await CreateAsync();
        await using var connection = await proof.Runtime.OpenConnectionAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await SelectTenantAsync(connection, transaction, "wholesale-alpha");
        string sql = operation switch
        {
            "insert" => """
                INSERT INTO sales.customer_profiles (id,organization_key,code,display_name,version)
                VALUES (gen_random_uuid(),'wholesale-beta','FOREIGN','Foreign',1)
                """,
            "owner-update" => """
                INSERT INTO sales.customer_profiles (id,organization_key,code,display_name,version)
                VALUES ('a2200000-0000-0000-0000-000000000001','wholesale-alpha','NEW','New',1);
                UPDATE sales.customer_profiles SET organization_key='wholesale-beta' WHERE code='NEW'
                """,
            "upsert" => $"""
                INSERT INTO sales.customer_profiles (id,organization_key,code,display_name,version)
                VALUES ('{SalesDemoSeed.BetaCustomerId:D}','wholesale-alpha','UPSERT','Foreign',1)
                ON CONFLICT (id) DO UPDATE SET display_name=EXCLUDED.display_name
                """,
            _ => throw new InvalidOperationException(),
        };
        await DeniedAsync(() => CommandAsync(connection, transaction, sql));
        await transaction.RollbackAsync(Token);
    }

    [Fact]
    public async Task RawAndExecuteUpdateDeleteUseDatabasePoliciesWithoutTrackedWriteGuards()
    {
        await using var proof = await CreateAsync();
        await using var provider = proof.Provider();
        await using var scope = TenantIsolationJourney.CreateScope(
            provider,
            "wholesale-alpha",
            "application-alpha"
        );
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await database.EstablishTenantDatabaseContextAsync(
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>(),
            "app.tenant_key",
            Token
        );
        Assert.Equal(
            0,
            await database
                .DraftOrders.IgnoreQueryFilters()
                .Where(row => row.OrganizationKey == "wholesale-beta")
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.Code, "FORBIDDEN"),
                    Token
                )
        );
        Assert.Equal(
            0,
            await database
                .DraftOrderLines.IgnoreQueryFilters()
                .Where(row => row.OrganizationKey == "wholesale-beta")
                .ExecuteDeleteAsync(Token)
        );
        Assert.Equal(
            1,
            await database
                .DraftOrderLines.IgnoreQueryFilters()
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Quantity, 2), Token)
        );
        Assert.Equal(
            0,
            await database.Database.ExecuteSqlRawAsync(
                "DELETE FROM sales.customer_addresses WHERE organization_key='wholesale-beta'",
                Token
            )
        );
        Assert.Empty(database.ChangeTracker.Entries());
        await transaction.RollbackAsync(Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("malformed-for-this-demo")]
    public async Task EmptyOrUnknownNativeContextHasNoProtectedRows(string tenant)
    {
        await using var proof = await CreateAsync();
        await using var connection = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.Equal(0, await CountAsync(connection));
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await SelectTenantAsync(connection, transaction, tenant);
        Assert.Equal(0, await CountAsync(connection, transaction));
        await DeniedAsync(() =>
            CommandAsync(
                connection,
                transaction,
                """
                INSERT INTO sales.customer_profiles (id,organization_key,code,display_name,version)
                VALUES (gen_random_uuid(),'wholesale-alpha','NO-CONTEXT','No context',1)
                """
            )
        );
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tenantless")]
    [InlineData("no-transaction")]
    public async Task ApplicationSetupRejectsMissingContextAndNativeTransaction(string missing)
    {
        await using var proof = await CreateAsync();
        await using var provider = proof.Provider();
        await using var scope = provider.CreateAsyncScope();
        if (missing == "tenantless")
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
        else if (missing == "no-transaction")
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(new("wholesale-alpha")));

        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var tenancy = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        if (missing == "tenantless")
            await Assert.ThrowsAsync<TenantRequiredException>(() =>
                database.EstablishTenantDatabaseContextAsync(tenancy, "app.tenant_key", Token)
            );
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.EstablishTenantDatabaseContextAsync(tenancy, "app.tenant_key", Token)
            );

        await using var connection = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.Equal(0, await CountAsync(connection));
        await DeniedAsync(() =>
            CommandAsync(
                connection,
                null,
                """
                INSERT INTO sales.customer_profiles (id,organization_key,code,display_name,version)
                VALUES (gen_random_uuid(),'wholesale-alpha','MISSING','Missing context',1)
                """
            )
        );
    }

    [Fact]
    public async Task EnabledNoPolicyDeniesAndDisabledRlsIsAnExplicitExposureControl()
    {
        await using var proof = await CreateAsync();
        await proof.AdminAsync("DROP POLICY organization_isolation ON sales.draft_orders");
        await using (var connection = await proof.Runtime.OpenConnectionAsync(Token))
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            await SelectTenantAsync(connection, transaction, "wholesale-alpha");
            Assert.Equal(0, await CountAsync(connection, transaction));
        }

        await proof.AdminAsync("ALTER TABLE sales.draft_orders DISABLE ROW LEVEL SECURITY");
        await using var exposed = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.Equal(2, await CountAsync(exposed));
    }

    [Fact]
    public async Task ForceConstrainsOwnerReadsWhileSuperuserAndBypassRoleEscape()
    {
        await using var proof = await CreateAsync();
        await using (var owner = new NpgsqlConnection(proof.OwnerConnection))
        {
            await owner.OpenAsync(Token);
            Assert.Equal(0, await CountAsync(owner));
        }

        await using (var admin = new NpgsqlConnection(proof.AdminConnection))
        {
            await admin.OpenAsync(Token);
            Assert.Equal(2, await CountAsync(admin));
        }

        await proof.AdminAsync("ALTER TABLE sales.draft_orders NO FORCE ROW LEVEL SECURITY");
        await using (var owner = new NpgsqlConnection(proof.OwnerConnection))
        {
            await owner.OpenAsync(Token);
            Assert.Equal(2, await CountAsync(owner));
        }

        using var identifiers = new NpgsqlCommandBuilder();
        await proof.AdminAsync(
            $"ALTER ROLE {identifiers.QuoteIdentifier(proof.RuntimeRole)} BYPASSRLS"
        );
        await using var bypass = await proof.Runtime.OpenConnectionAsync(Token);
        Assert.Equal(2, await CountAsync(bypass));
        await bypass.CloseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SalesRowSecurity.VerifyRuntimeAsync(proof.Runtime, Token)
        );
    }

    [Fact]
    public async Task HiddenUniqueConstraintCanRevealExistenceWithoutReturningForeignRows()
    {
        await using var proof = await CreateAsync();
        await using var connection = await proof.Runtime.OpenConnectionAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await SelectTenantAsync(connection, transaction, "wholesale-alpha");
        Assert.Equal(
            0,
            await ValueAsync<int>(
                connection,
                transaction,
                "SELECT count(*) FROM sales.customer_profiles WHERE organization_key='wholesale-beta'"
            )
        );
        await DeniedAsync(
            () =>
                CommandAsync(
                    connection,
                    transaction,
                    $"""
                    INSERT INTO sales.customer_profiles (id,organization_key,code,display_name,version)
                    VALUES ('{SalesDemoSeed.BetaCustomerId:D}','wholesale-alpha','COLLISION','Collision',1)
                    """
                ),
            PostgresErrorCodes.UniqueViolation
        );
    }

    [Fact]
    public async Task RuntimeCredentialCanForgeTheTenantSettingAndRlsDoesNotAuthenticateIt()
    {
        await using var proof = await CreateAsync();
        await using var provider = proof.Provider();
        await using var scope = TenantIsolationJourney.CreateScope(
            provider,
            "wholesale-alpha",
            "application-alpha"
        );
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var tenancy = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await database.EstablishTenantDatabaseContextAsync(tenancy, "app.tenant_key", Token);
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await SelectTenantAsync(
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction(),
            "wholesale-beta"
        );
        Assert.Equal(
            "wholesale-beta",
            await ValueAsync<string>(
                connection,
                (NpgsqlTransaction)transaction.GetDbTransaction(),
                "SELECT organization_key FROM sales.draft_orders"
            )
        );
        Assert.Equal("wholesale-alpha", tenancy.Current.RequireTenant().Value);
    }
}
