using System.Net.Http.Json;
using Gloaming.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Access.Persistence;
using ModulithFoundry.Samples.Wholesale.Api;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    [Fact]
    public async Task CancelledAddressUpdateAfterCustomerSaveRollsBackAndFreshRequestRecovers()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartAsync(beforeContext: app =>
            app.Use(
                async (context, next) =>
                {
                    try
                    {
                        await next(context);
                    }
                    finally
                    {
                        if (context.Request.Method == "PUT")
                            finished.TrySetResult();
                    }
                }
            )
        );
        using var client = SecureClient(app);
        var before = await ReadProfileAsync(app, client);
        var session = await IssueTokenAsync(app, client);
        await using var blocker = new NpgsqlConnection(
            app.Configuration.GetConnectionString("Access")
        );
        await blocker.OpenAsync(Token);
        await using var transaction = await blocker.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "LOCK TABLE sales.customer_addresses IN SHARE MODE",
                blocker,
                transaction
            )
        )
            await command.ExecuteNonQueryAsync(Token);
        using var request = EditRequest(session);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task<System.Net.Http.HttpResponseMessage> operation = client.SendAsync(
            request,
            cancellation.Token
        );
        try
        {
            // SHARE permits the earlier SELECT; the address UPDATE waits after the customer save.
            await WaitForBlockedUpdatesAsync(app, "customer_addresses", 1);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            // A fresh read while the lock still permits SELECT proves rollback completed.
            Assert.Equal(before, await ReadProfileAsync(app, client));
        }
        finally
        {
            await cancellation.CancelAsync();
            await transaction.RollbackAsync(CancellationToken.None);
        }
        using var retry = EditRequest(session);
        using var response = await client.SendAsync(retry, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            2,
            (await response.Content.ReadFromJsonAsync<CustomerProfile>(Token))!.Version
        );
    }

    private static async Task WaitForBlockedUpdatesAsync(
        WebApplication app,
        string table,
        int count
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        // pg_stat_activity must be read outside the lock transaction's cached statistics snapshot.
        await using var observer = new NpgsqlConnection(
            app.Configuration.GetConnectionString("Access")
        );
        await observer.OpenAsync(deadline.Token);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid()
                AND state = 'active' AND wait_event_type = 'Lock'
                AND query LIKE @statement
            """,
            observer
        );
        command.Parameters.AddWithValue("statement", "%UPDATE sales." + table + "%");
        while (
            Convert.ToInt64(
                await command.ExecuteScalarAsync(deadline.Token),
                System.Globalization.CultureInfo.InvariantCulture
            ) < count
        )
            await Task.Delay(25, deadline.Token);
    }

    [Fact]
    public async Task SalesMigrationPreservesExistingAccessInventoryRowsAndHistories()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        // Apply/seed the prior modules before Sales exists; applying it cannot replace their histories.
        var accessOptions = new DbContextOptionsBuilder<AccessDbContext>();
        AccessDatabase.Configure(accessOptions, connection);
        await using var access = new AccessDbContext(accessOptions.Options);
        await access.Database.MigrateAsync(Token);
        ModulithFoundry.Samples.Wholesale.Access.AccessDemoSeed.Stage(access);
        await access.SaveChangesAsync(Token);
        using var tenant = new TenantContextAccessor();
        tenant.Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        var inventoryOptions = new DbContextOptionsBuilder<InventoryDbContext>();
        InventoryDatabase.Configure(inventoryOptions, connection);
        await using var inventory = new InventoryDbContext(inventoryOptions.Options, tenant);
        await inventory.Database.MigrateAsync(Token);
        InventoryDemoSeed.Stage(inventory, 42);
        await inventory.SaveChangesAsync(Token);
        string[] accessHistory = (await access.Database.GetAppliedMigrationsAsync(Token)).ToArray();
        string[] inventoryHistory = (
            await inventory.Database.GetAppliedMigrationsAsync(Token)
        ).ToArray();
        var salesOptions = new DbContextOptionsBuilder<SalesDbContext>();
        SalesDatabase.Configure(salesOptions, connection);
        await using var sales = new SalesDbContext(salesOptions.Options, tenant);
        await sales.Database.MigrateAsync(Token);
        Assert.False(sales.Database.HasPendingModelChanges());
        Assert.Equal(
            sales.Database.GetMigrations(),
            await sales.Database.GetAppliedMigrationsAsync(Token)
        );
        Assert.Equal(accessHistory, await access.Database.GetAppliedMigrationsAsync(Token));
        Assert.Equal(inventoryHistory, await inventory.Database.GetAppliedMigrationsAsync(Token));
        await using var app = await StartAsync(connectionString: connection, initialize: false);
        using var client = SecureClient(app);
        using var request = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "application-alpha",
            (await response.Content.ReadFromJsonAsync<TenantIdentityResponse>(Token))!.ActorId
        );
        Assert.Equal(
            42,
            (
                await client.GetFromJsonAsync<CatalogResponse>(
                    "/organizations/north-supply/catalog",
                    Token
                )
            )!
                .Stock
                .AvailableQuantity
        );
    }

    [Theory]
    [InlineData("foreign-parent", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("zero-version", PostgresErrorCodes.CheckViolation)]
    [InlineData("duplicate-code", PostgresErrorCodes.UniqueViolation)]
    public async Task SalesMigrationEnforcesTenantParentVersionAndCodeRules(
        string rule,
        string state
    )
    {
        await using var app = await StartAsync();
        string sql = rule switch
        {
            "foreign-parent" =>
                "UPDATE sales.customer_addresses SET customer_id = '10000000-0000-0000-0000-000000000002' WHERE organization_key = 'wholesale-alpha'",
            "zero-version" =>
                "UPDATE sales.customer_profiles SET version = 0 WHERE organization_key = 'wholesale-alpha'",
            "duplicate-code" =>
                "INSERT INTO sales.customer_profiles (id, organization_key, code, display_name, version) VALUES (gen_random_uuid(), 'wholesale-alpha', 'DEMO-CUSTOMER', 'Duplicate', 1)",
            _ => throw new ArgumentOutOfRangeException(nameof(rule)),
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(app, sql));
        Assert.Equal(state, error.SqlState);
        using var client = SecureClient(app);
        Assert.Equal("Alpha Customer", (await ReadProfileAsync(app, client)).DisplayName);
    }
}
