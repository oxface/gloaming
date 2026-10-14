using System.Diagnostics;
using ConsumerRoot.Catalog;
using ConsumerRoot.Catalog.Contracts;
using ConsumerRoot.Host;
using Gloaming.ActorIdentity;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ConsumerRoot.Adoption.Tests;

public sealed class CatalogJourneyTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task Explicit_initialization_and_tenant_owned_catalog_work_in_a_fresh_database()
    {
        string admin =
            Environment.GetEnvironmentVariable("CATALOG_TEST_ADMIN_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set CATALOG_TEST_ADMIN_CONNECTION_STRING to a disposable local PostgreSQL server. Tests create and drop their own database."
            );
        string name = "adoption_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connection, $"CREATE DATABASE \"{name}\"");
        string target = new NpgsqlConnectionStringBuilder(admin)
        {
            Database = name,
            Pooling = false,
        }.ConnectionString;
        try
        {
            await RunHostAsync(target);
            await using var inspection = new NpgsqlConnection(target);
            await inspection.OpenAsync(TestContext.Current.CancellationToken);
            Assert.Empty(await TablesAsync(inspection));
            await RunHostAsync(target, "migrate");
            Assert.Equal(
                ["catalog.__EFMigrationsHistory", "catalog.items"],
                await TablesAsync(inspection)
            );
            using var services = Composition
                .CreateServices(target)
                .BuildServiceProvider(
                    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
                );
            await using (var scope = services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                Assert.False(database.Database.HasPendingModelChanges());
                Assert.Empty(
                    await database.Database.GetPendingMigrationsAsync(
                        TestContext.Current.CancellationToken
                    )
                );
                Assert.Equal(0L, await CountAsync(inspection)); // migration never seeds
            }

            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();
            await SeedAsync(services, "tenant-a", first, "Alpha");
            await SeedAsync(services, "tenant-b", second, "Beta");
            Assert.Equal([new CatalogItem(first, "Alpha")], await ReadAsync(services, "tenant-a"));
            Assert.Equal([new CatalogItem(second, "Beta")], await ReadAsync(services, "tenant-b"));

            await using (var scope = services.CreateAsyncScope())
            {
                EstablishTestContext(scope.ServiceProvider, "tenant-a");
                var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                database.Items.Add(
                    new ItemRow
                    {
                        Id = Guid.NewGuid(),
                        TenantKey = "tenant-b",
                        Name = "Rejected",
                    }
                );
                var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                    database.SaveChangesAsync(TestContext.Current.CancellationToken)
                );
                Assert.Equal(TenantOwnershipFailure.ForeignOwner, failure.Reason);
            }
            await using (var scope = services.CreateAsyncScope())
            {
                EstablishTestContext(scope.ServiceProvider, "tenant-a");
                var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                // Detached input lies about ownership; the stored-owner predicate protects Beta.
                database.Items.Update(
                    new ItemRow
                    {
                        Id = second,
                        TenantKey = "tenant-a",
                        Name = "Forged",
                    }
                );
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                    database.SaveChangesAsync(TestContext.Current.CancellationToken)
                );
            }
            Assert.Equal([new CatalogItem(second, "Beta")], await ReadAsync(services, "tenant-b"));
            Assert.Equal(2L, await CountAsync(inspection));
        }
        finally
        {
            await ExecuteAsync(connection, $"DROP DATABASE \"{name}\" WITH (FORCE)");
        }
    }

    [Theory]
    [InlineData("missing-actor")]
    [InlineData("missing-tenant")]
    [InlineData("anonymous")]
    [InlineData("tenantless")]
    public async global::System.Threading.Tasks.Task Required_context_fails_before_database_access(
        string scenario
    )
    {
        // No server exists here: context failure must happen before a database call.
        using var services = Composition
            .CreateServices("Host=127.0.0.1;Port=1;Database=unused;Timeout=1")
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        if (scenario != "missing-actor")
        {
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(
                    new ActorContext(
                        scenario == "anonymous"
                            ? Actor.Anonymous
                            : Actor.System(new ActorId("test-only"))
                    )
                );
        }
        if (scenario != "missing-tenant")
        {
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(
                    scenario == "tenantless"
                        ? TenantContext.Tenantless()
                        : TenantContext.ForTenant(new TenantId("tenant-a"))
                );
        }
        var queries = scope.ServiceProvider.GetRequiredService<ICatalogQueries>();
        if (scenario == "anonymous")
        {
            await Assert.ThrowsAsync<IdentifiedActorRequiredException>(() =>
                queries.ListAsync(TestContext.Current.CancellationToken)
            );
        }
        else if (scenario == "tenantless")
        {
            await Assert.ThrowsAsync<TenantRequiredException>(() =>
                queries.ListAsync(TestContext.Current.CancellationToken)
            );
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queries.ListAsync(TestContext.Current.CancellationToken)
            );
        }
    }

    private static void EstablishTestContext(IServiceProvider services, string tenant)
    {
        // TEST ONLY: these fixtures stand in for completed consumer authentication/admission.
        services
            .GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(Actor.System(new ActorId("catalog-test"))));
        services
            .GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(tenant)));
    }

    private static async global::System.Threading.Tasks.Task SeedAsync(
        ServiceProvider services,
        string tenant,
        Guid id,
        string name
    )
    {
        await using var scope = services.CreateAsyncScope();
        EstablishTestContext(scope.ServiceProvider, tenant);
        var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );
        database.Items.Add(
            new ItemRow
            {
                Id = id,
                TenantKey = tenant,
                Name = name,
            }
        );
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    private static async global::System.Threading.Tasks.Task<IReadOnlyList<CatalogItem>> ReadAsync(
        ServiceProvider services,
        string tenant
    )
    {
        await using var scope = services.CreateAsyncScope();
        EstablishTestContext(scope.ServiceProvider, tenant);
        return await scope
            .ServiceProvider.GetRequiredService<ICatalogQueries>()
            .ListAsync(TestContext.Current.CancellationToken);
    }

    private static async global::System.Threading.Tasks.Task RunHostAsync(
        string connectionString,
        params string[] arguments
    )
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(Composition).Assembly.Location);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["CATALOG_CONNECTION_STRING"] = connectionString;
        using var process =
            Process.Start(start) ?? throw new InvalidOperationException("Host did not start.");
        global::System.Threading.Tasks.Task<string> stdout = process.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken
        );
        global::System.Threading.Tasks.Task<string> stderr = process.StandardError.ReadToEndAsync(
            TestContext.Current.CancellationToken
        );
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    private static async global::System.Threading.Tasks.Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql
    )
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async global::System.Threading.Tasks.Task<string[]> TablesAsync(
        NpgsqlConnection connection
    )
    {
        await using var command = new NpgsqlCommand(
            "SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY 1",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken
        );
        var tables = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }
        return tables.ToArray();
    }

    private static async global::System.Threading.Tasks.Task<long> CountAsync(
        NpgsqlConnection connection
    )
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM catalog.items",
            connection
        );
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
