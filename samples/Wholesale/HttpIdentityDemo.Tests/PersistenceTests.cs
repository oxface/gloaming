using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Gloaming.ActorIdentity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Access.Contracts;
using ModulithFoundry.Samples.Wholesale.Access.Persistence;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.Tests;

public sealed partial class CompositionTests
{
    [Theory]
    [InlineData("external-pair", PostgresErrorCodes.UniqueViolation)]
    [InlineData("current-membership", PostgresErrorCodes.UniqueViolation)]
    [InlineData("organization-slug", PostgresErrorCodes.UniqueViolation)]
    [InlineData("external-user", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("membership-user", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("membership-organization", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("zero-status", PostgresErrorCodes.CheckViolation)]
    [InlineData("undefined-status", PostgresErrorCodes.CheckViolation)]
    [InlineData("noncanonical-slug", PostgresErrorCodes.CheckViolation)]
    public async Task ActualMigrationEnforcesAccessIdentityAndMembershipInvariants(
        string invariant,
        string sqlState
    )
    {
        await using var app = await StartAsync();
        string sql = invariant switch
        {
            "external-pair" => """
                INSERT INTO access.external_identities (issuer, subject, user_id)
                VALUES ('https://identity.test', 'shared-subject', 'application-beta')
                """,
            "current-membership" => """
                INSERT INTO access.memberships (id, user_id, organization_id, status)
                VALUES (gen_random_uuid(), 'application-alpha', 'wholesale-alpha', 2)
                """,
            "organization-slug" => """
                INSERT INTO access.organizations (id, slug) VALUES ('another', 'north-supply')
                """,
            "external-user" => """
                INSERT INTO access.external_identities (issuer, subject, user_id)
                VALUES ('https://unlinked.test', 'account', 'missing-user')
                """,
            "membership-user" => """
                INSERT INTO access.memberships (id, user_id, organization_id, status)
                VALUES (gen_random_uuid(), 'missing-user', 'wholesale-alpha', 1)
                """,
            "membership-organization" => """
                INSERT INTO access.memberships (id, user_id, organization_id, status)
                VALUES (gen_random_uuid(), 'application-alpha', 'missing-organization', 1)
                """,
            "zero-status" =>
                "UPDATE access.memberships SET status = 0 WHERE user_id = 'application-alpha'",
            "undefined-status" =>
                "UPDATE access.memberships SET status = 4 WHERE user_id = 'application-alpha'",
            "noncanonical-slug" =>
                "UPDATE access.organizations SET slug = 'NORTH-SUPPLY' WHERE id = 'wholesale-alpha'",
            _ => throw new ArgumentOutOfRangeException(nameof(invariant)),
        };
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(app, sql));
        Assert.Equal(sqlState, failure.SqlState);
        using var client = app.GetTestClient();
        using var request = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            new TenantIdentityResponse(ActorKind.Human, "application-alpha", "wholesale-alpha"),
            await response.Content.ReadFromJsonAsync<TenantIdentityResponse>(Token)
        );
    }

    [Fact]
    public async Task RemovedMembershipHistoryCanCoexistWithOneCurrentMembership()
    {
        await using var app = await StartAsync();
        await ExecuteAsync(
            app,
            """
            INSERT INTO access.memberships (id, user_id, organization_id, status) VALUES
                (gen_random_uuid(), 'application-alpha', 'wholesale-alpha', 3),
                (gen_random_uuid(), 'application-alpha', 'wholesale-alpha', 3)
            """
        );
        await using var scope = app.Services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<IApplicationAccess>();
        Assert.Equal(
            new OrganizationId("wholesale-alpha"),
            await access.ResolveMemberOrganizationAsync(
                new UserId("application-alpha"),
                "north-supply",
                Token
            )
        );
    }

    [Fact]
    public async Task CancelledBlockedAdmissionPublishesNothingAndFreshRequestRecovers()
    {
        var finished = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int downstream = 0;
        await using var app = await StartAsync(
            beforeContext: app => ObservePublication(app, value => finished.TrySetResult(value)),
            afterContext: app =>
                app.Use(
                    async (context, next) =>
                    {
                        downstream++;
                        await next(context);
                    }
                )
        );
        await using var blocker = new NpgsqlConnection(
            app.Configuration.GetConnectionString("Access")
        );
        await blocker.OpenAsync(Token);
        await using var transaction = await blocker.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "LOCK TABLE access.memberships IN ACCESS EXCLUSIVE MODE",
                blocker,
                transaction
            )
        )
            await command.ExecuteNonQueryAsync(Token);
        using var client = app.GetTestClient();
        using var request = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task<HttpResponseMessage> operation = client.SendAsync(request, cancellation.Token);
        // Wait for the real admission statement to reach PostgreSQL, rather than cancelling before ingress.
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            // Use autocommit reads: pg_stat_activity is cached within the lock transaction.
            await using var observer = new NpgsqlConnection(
                app.Configuration.GetConnectionString("Access")
            );
            await observer.OpenAsync(deadline.Token);
            await using var command = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity
                    WHERE datname = current_database() AND pid <> pg_backend_pid()
                    AND state = 'active' AND wait_event_type = 'Lock' AND query LIKE '%memberships%'
                )
                """,
                observer
            );
            while (!Equals(true, await command.ExecuteScalarAsync(deadline.Token)))
                await Task.Delay(25, deadline.Token);
        }
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.False(await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), Token));
        Assert.Equal(0, downstream);
        await transaction.RollbackAsync(Token);
        using var retry = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var recovered = await client.SendAsync(retry, Token);
        recovered.EnsureSuccessStatusCode();
        Assert.Equal(1, downstream);
    }

    [Fact]
    public async Task OrdinaryHttpStartupDoesNotMigrateOrSeedAnEmptyDatabase()
    {
        await using var app = await StartAsync(initialize: false);
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        Assert.Empty(await database.Database.GetAppliedMigrationsAsync(Token));
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Empty(await inventory.Database.GetAppliedMigrationsAsync(Token));
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        Assert.Empty(await sales.Database.GetAppliedMigrationsAsync(Token));
        using var client = app.GetTestClient();
        using var healthy = await client.GetAsync("/health", Token);
        healthy.EnsureSuccessStatusCode();
        using var unavailable = await client.GetAsync("/organizations/north-supply/catalog", Token);
        Assert.Equal(HttpStatusCode.InternalServerError, unavailable.StatusCode);
        Assert.Empty(await database.Database.GetAppliedMigrationsAsync(Token));
        Assert.Empty(await inventory.Database.GetAppliedMigrationsAsync(Token));
        Assert.Empty(await sales.Database.GetAppliedMigrationsAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiniteSetupNeedsNoOidcAndItsRowsAreUsedByHttpHost(bool fullDemo)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(DemoComposition).Assembly.Location);
        start.ArgumentList.Add(fullDemo ? "--initialize-demo" : "--initialize-access");
        start.Environment["ConnectionStrings__Access"] = connection;
        start.Environment.Remove("Oidc__Authority");
        start.Environment.Remove("Oidc__ClientId");
        start.Environment.Remove("Oidc__ClientSecret");
        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Could not start Access setup.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(Token);
        Task<string> errors = process.StandardError.ReadToEndAsync(Token);
        try
        {
            await process.WaitForExitAsync(Token).WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.True(process.ExitCode == 0, await errors);
            Assert.Contains(
                fullDemo
                    ? "Wholesale demo initialized; HTTP host was not started."
                    : "Access demo initialized; HTTP host was not started.",
                await output
            );
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        await using var app = await StartAsync(connectionString: connection, initialize: false);
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        Assert.Equal(
            database.Database.GetMigrations(),
            await database.Database.GetAppliedMigrationsAsync(Token)
        );
        using var client = app.GetTestClient();
        using var request = Request(
            app,
            "/organizations/north-supply/identity",
            "https://identity.test"
        );
        using var admitted = await client.SendAsync(request, Token);
        admitted.EnsureSuccessStatusCode();
        Assert.Equal(
            new TenantIdentityResponse(ActorKind.Human, "application-alpha", "wholesale-alpha"),
            await admitted.Content.ReadFromJsonAsync<TenantIdentityResponse>(Token)
        );
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        if (!fullDemo)
        {
            Assert.Empty(await sales.Database.GetAppliedMigrationsAsync(Token));
            Assert.Empty(await inventory.Database.GetAppliedMigrationsAsync(Token));
            using var unavailable = await client.GetAsync(
                "/organizations/south-supply/catalog",
                Token
            );
            Assert.Equal(HttpStatusCode.InternalServerError, unavailable.StatusCode);
            return;
        }
        Assert.Equal(
            inventory.Database.GetMigrations(),
            await inventory.Database.GetAppliedMigrationsAsync(Token)
        );
        Assert.Equal(
            sales.Database.GetMigrations(),
            await sales.Database.GetAppliedMigrationsAsync(Token)
        );
        using var profileRequest = Request(
            app,
            $"/organizations/north-supply/customers/{SalesDemoSeed.AlphaCustomerId}/profile",
            "https://identity.test"
        );
        using var profileResponse = await client.SendAsync(profileRequest, Token);
        Assert.Equal(
            "Alpha Customer",
            (await profileResponse.Content.ReadFromJsonAsync<CustomerProfile>(Token))!.DisplayName
        );
        Assert.Equal(
            7,
            (
                await client.GetFromJsonAsync<CatalogResponse>(
                    "/organizations/south-supply/catalog",
                    Token
                )
            )!
                .Stock
                .AvailableQuantity
        );
    }
}
