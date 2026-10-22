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

public sealed class RuntimeTests
{
    private static readonly string[] EphemeralArguments =
    [
        "LocalDevelopment:UseDataVolume=false",
        "LocalDevelopment:UseLocalIdentityProvider=false",
        "LocalDevelopment:UseSalesRowSecurity=false",
    ];

    [Fact]
    public async Task ExplicitSetupEnablesRealCatalogAndDatabaseOutageLeavesApiAlive()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        CancellationToken cancellation = deadline.Token;
        await using var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostMarker>(
                EphemeralArguments,
                cancellation
            );
        builder.Configuration["Parameters:postgres-password"] = "disposable-topology-password";
        builder.Configuration["Parameters:oidc-authority"] = "https://identity.test";
        builder.Configuration["Parameters:oidc-client-id"] = "inert-topology-client";
        builder.Configuration["DcpPublisher:RandomizePorts"] = "true";
        await using var app = await builder.BuildAsync(cancellation);
        await app.StartAsync(cancellation);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("wholesale", cancellation);
        await app.ResourceNotifications.WaitForResourceAsync(
            "api",
            KnownResourceStates.Running,
            cancellation
        );
        Assert.True(app.ResourceNotifications.TryGetCurrentState("demo-setup", out var setup));
        Assert.NotEqual(KnownResourceStates.Running, setup.Snapshot.State?.Text);
        Assert.NotEqual(KnownResourceStates.Finished, setup.Snapshot.State?.Text);
        using var client = app.CreateHttpClient("api", "http");
        using var liveBefore = await client.GetAsync("/health/live", cancellation);
        using var readyBefore = await client.GetAsync("/health/ready", cancellation);
        Assert.Equal(HttpStatusCode.OK, liveBefore.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyBefore.StatusCode);
        await using (
            var connection = new NpgsqlConnection(
                await app.GetConnectionStringAsync("wholesale", cancellation)
            )
        )
        {
            await connection.OpenAsync(cancellation);
            await using var command = new NpgsqlCommand(
                """
                SELECT to_regclass('access.users') IS NULL
                    AND to_regclass('inventory.stock_availability') IS NULL
                    AND to_regclass('sales.customer_profiles') IS NULL
                """,
                connection
            );
            Assert.Equal(true, await command.ExecuteScalarAsync(cancellation));
        }
        var commands = app.Services.GetRequiredService<ResourceCommandService>();
        var started = await commands.ExecuteCommandAsync(
            "demo-setup",
            KnownResourceCommands.StartCommand,
            cancellation
        );
        Assert.True(started.Success, started.Message);
        await app.ResourceNotifications.WaitForResourceAsync(
            "demo-setup",
            KnownResourceStates.Finished,
            cancellation
        );
        Assert.True(app.ResourceNotifications.TryGetCurrentState("demo-setup", out setup));
        Assert.Equal(0, setup.Snapshot.ExitCode);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellation);
        using var ready = await client.GetAsync("/health/ready", cancellation);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        foreach (
            var (organization, expected) in new[] { ("north-supply", 42), ("south-supply", 7) }
        )
            Assert.Equal(
                expected,
                (
                    await client.GetFromJsonAsync<CatalogResponse>(
                        $"/organizations/{organization}/catalog",
                        cancellation
                    )
                )!
                    .Stock
                    .AvailableQuantity
            );
        var stopped = await commands.ExecuteCommandAsync(
            "postgres",
            KnownResourceCommands.StopCommand,
            cancellation
        );
        Assert.True(stopped.Success, stopped.Message);
        await app.ResourceNotifications.WaitForResourceAsync(
            "postgres",
            KnownResourceStates.Exited,
            cancellation
        );
        using var unready = await client.GetAsync("/health/ready", cancellation);
        using var alive = await client.GetAsync("/health/live", cancellation);
        using var failedRead = await client.GetAsync(
            "/organizations/north-supply/catalog",
            cancellation
        );
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, failedRead.StatusCode);
        await app.StopAsync(cancellation);
    }
}
