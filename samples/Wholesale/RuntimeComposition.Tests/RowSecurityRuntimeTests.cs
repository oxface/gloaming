using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Api;
using ModulithFoundry.Samples.Wholesale.AppHost;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.RuntimeComposition.Tests;

public sealed class RowSecurityRuntimeTests
{
    private const string AccessPassword = "access runtime;\"quotes'and\\slashes";
    private const string SalesPassword = "sales runtime;\"quotes'and\\slashes";

    [Fact(Timeout = 180_000)]
    public async Task ExplicitRlsSetupCreatesRestrictedRuntimeAndStartsApiWithoutOwnerCredentials()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        CancellationToken cancellation = deadline.Token;
        await using var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostMarker>(
                [
                    "LocalDevelopment:UseDataVolume=false",
                    "LocalDevelopment:UseLocalIdentityProvider=false",
                    "LocalDevelopment:UseSalesRowSecurity=true",
                ],
                cancellation
            );
        builder.Configuration["Parameters:postgres-password"] = "a27-disposable-admin-password";
        builder.Configuration["Parameters:access-runtime-password"] = AccessPassword;
        builder.Configuration["Parameters:sales-runtime-password"] = SalesPassword;
        builder.Configuration["Parameters:oidc-authority"] = "https://identity.test";
        builder.Configuration["Parameters:oidc-client-id"] = "inert-a27-client";
        builder.Configuration["DcpPublisher:RandomizePorts"] = "true";
        await using var app = await builder.BuildAsync(cancellation);
        await app.StartAsync(cancellation);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("wholesale", cancellation);
        Assert.True(app.ResourceNotifications.TryGetCurrentState("api", out var api));
        Assert.NotEqual(KnownResourceStates.Running, api.Snapshot.State?.Text);
        string admin = (await app.GetConnectionStringAsync("wholesale", cancellation))!;
        await using var observer = new NpgsqlConnection(admin);
        await observer.OpenAsync(cancellation);
        await using (
            var check = new NpgsqlCommand(
                "SELECT to_regclass('sales.customer_profiles') IS NULL AND NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname IN ('wholesale_access', 'wholesale_sales'))",
                observer
            )
        )
            Assert.Equal(true, await check.ExecuteScalarAsync(cancellation));

        var commands = app.Services.GetRequiredService<ResourceCommandService>();
        var setup = await commands.ExecuteCommandAsync(
            "demo-setup",
            KnownResourceCommands.StartCommand,
            cancellation
        );
        Assert.True(setup.Success, setup.Message);
        await app.ResourceNotifications.WaitForResourceAsync(
            "demo-setup",
            KnownResourceStates.Finished,
            cancellation
        );
        Assert.True(app.ResourceNotifications.TryGetCurrentState("demo-setup", out var finished));
        Assert.Equal(0, finished.Snapshot.ExitCode);
        var started = await commands.ExecuteCommandAsync(
            "api",
            KnownResourceCommands.StartCommand,
            cancellation
        );
        Assert.True(started.Success, started.Message);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellation);
        using var client = app.CreateHttpClient("api", "http");
        foreach (var (slug, expected) in new[] { ("north-supply", 42), ("south-supply", 7) })
            Assert.Equal(
                expected,
                (
                    await client.GetFromJsonAsync<CatalogResponse>(
                        $"/organizations/{slug}/catalog",
                        cancellation
                    )
                )!
                    .Stock
                    .AvailableQuantity
            );

        using var challenge = await client.GetAsync(
            "/organizations/north-supply/draft-orders",
            cancellation
        );
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        var salesSettings = new NpgsqlConnectionStringBuilder(
            (await app.GetConnectionStringAsync("sales-runtime", cancellation))!
        )
        {
            Password = SalesPassword,
        };
        var accessSettings = new NpgsqlConnectionStringBuilder(
            (await app.GetConnectionStringAsync("access-runtime", cancellation))!
        )
        {
            Password = AccessPassword,
        };
        await SalesHttpRowSecurity.VerifyRuntimeAsync(
            salesSettings.ConnectionString,
            accessSettings.ConnectionString,
            cancellation
        );
        await using (var accessConnection = new NpgsqlConnection(accessSettings.ConnectionString))
        {
            await accessConnection.OpenAsync(cancellation);
            await using var deniedSchema = new NpgsqlCommand(
                "SELECT NOT has_schema_privilege(current_user, 'sales', 'USAGE')",
                accessConnection
            );
            Assert.Equal(true, await deniedSchema.ExecuteScalarAsync(cancellation));
        }

        await using (var runtime = new NpgsqlConnection(salesSettings.ConnectionString))
        {
            await runtime.OpenAsync(cancellation);
            await using (
                var missing = new NpgsqlCommand(
                    "SELECT count(*) FROM sales.customer_profiles",
                    runtime
                )
            )
                Assert.Equal(0L, await missing.ExecuteScalarAsync(cancellation));

            await using var transaction = await runtime.BeginTransactionAsync(cancellation);
            await using (
                var setting = new NpgsqlCommand(
                    "SELECT set_config('app.tenant_key', 'wholesale-alpha', true)",
                    runtime,
                    transaction
                )
            )
                await setting.ExecuteScalarAsync(cancellation);

            await using (
                var read = new NpgsqlCommand(
                    "SELECT display_name FROM sales.customer_profiles",
                    runtime,
                    transaction
                )
            )
                Assert.Equal("Alpha Customer", await read.ExecuteScalarAsync(cancellation));

            await using var foreign = new NpgsqlCommand(
                "UPDATE sales.customer_profiles SET organization_key = 'wholesale-beta' WHERE organization_key = 'wholesale-alpha'",
                runtime,
                transaction
            );
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                foreign.ExecuteNonQueryAsync(cancellation)
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        }

        await app.StopAsync(cancellation);
    }
}
