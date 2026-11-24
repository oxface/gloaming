using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Gloaming.Samples.Wholesale.Api;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.RuntimeComposition.Tests;

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
        await using var builder = await WholesaleTestHost.CreateAsync(
            EphemeralArguments,
            cancellation
        );
        builder.Configuration["Parameters:postgres-password"] = "disposable-topology-password";
        builder.Configuration["Parameters:oidc-authority"] = "https://identity.test";
        builder.Configuration["Parameters:oidc-client-id"] = "inert-topology-client";
        await using var app = await builder.BuildAsync(cancellation);
        await using var evidence = new RuntimeEvidence(app, "database-outage");
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
        await FiniteResource.RunAsync(app, "demo-setup", cancellation);
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
        var commands = app.Services.GetRequiredService<ResourceCommandService>();
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
    }
}
