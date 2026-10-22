using System.Diagnostics;
using ConsumerRoot.CatalogWorker;

namespace ConsumerRoot.Adoption.Tests;

public sealed partial class CatalogJourneyTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task Separate_worker_reads_only_its_tenant_and_stops_after_repeated_fresh_scopes()
    {
        await WithDatabaseAsync(
            async (services, connection) =>
            {
                await CreateItemAsync(services, "tenant-a", Guid.NewGuid(), "Alpha", 40);
                await CreateItemAsync(services, "tenant-b", Guid.NewGuid(), "Beta one", 50);
                await CreateItemAsync(services, "tenant-b", Guid.NewGuid(), "Beta two", 60);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using (var once = StartWorker(connection, "tenant-b", true))
                {
                    var stdout = once.StandardOutput.ReadToEndAsync(timeout.Token);
                    var stderr = once.StandardError.ReadToEndAsync(timeout.Token);
                    try
                    {
                        await once.WaitForExitAsync(timeout.Token);
                        Assert.True(once.ExitCode == 0, await stderr);
                        Assert.Contains(
                            "Catalog report tenant=tenant-b total=2 items=2",
                            await stdout,
                            StringComparison.Ordinal
                        );
                    }
                    finally
                    {
                        if (!once.HasExited)
                            once.Kill(entireProcessTree: true);
                    }
                }

                using var periodic = StartWorker(connection, "tenant-a", false);
                var errors = periodic.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    int reports = 0;
                    while (reports < 2)
                    {
                        string? line = await periodic.StandardOutput.ReadLineAsync(timeout.Token);
                        Assert.NotNull(line);
                        if (line.Contains("Catalog report", StringComparison.Ordinal))
                        {
                            Assert.Equal("Catalog report tenant=tenant-a total=1 items=1", line);
                            reports++;
                        }
                    }

                    // The supported generator platform is Linux. Exercise native generic-host SIGTERM.
                    using var signal = Process.Start(
                        new ProcessStartInfo("kill")
                        {
                            ArgumentList =
                            {
                                "-TERM",
                                periodic.Id.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture
                                ),
                            },
                        }
                    );
                    Assert.NotNull(signal);
                    await signal.WaitForExitAsync(timeout.Token);
                    await periodic.WaitForExitAsync(timeout.Token);
                    Assert.True(periodic.ExitCode == 0, await errors);
                }
                finally
                {
                    if (!periodic.HasExited)
                        periodic.Kill(entireProcessTree: true);
                }
            }
        );
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Worker_refuses_missing_mandate_and_reports_database_failure()
    {
        using var missing = StartWorker(
            "Host=127.0.0.1;Port=1;Database=unused;Timeout=1",
            "",
            true
        );
        var output = missing.StandardOutput.ReadToEndAsync(Token);
        var errors = missing.StandardError.ReadToEndAsync(Token);
        await missing.WaitForExitAsync(Token);
        Assert.Equal(2, missing.ExitCode);
        Assert.Contains("authorized deployment mandate", await errors, StringComparison.Ordinal);
        await output;

        using var unavailable = StartWorker(
            "Host=127.0.0.1;Port=1;Database=unused;Timeout=1",
            "tenant-a",
            true
        );
        var failedOutput = unavailable.StandardOutput.ReadToEndAsync(Token);
        var failedErrors = unavailable.StandardError.ReadToEndAsync(Token);
        await unavailable.WaitForExitAsync(Token);
        Assert.Equal(1, unavailable.ExitCode);
        Assert.DoesNotContain(
            "Catalog report tenant=",
            await failedOutput + await failedErrors,
            StringComparison.Ordinal
        );
    }

    private static Process StartWorker(string connection, string tenant, bool once)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(WorkerMarker).Assembly.Location);
        if (once)
            start.ArgumentList.Add("--once");

        start.Environment["CATALOG_CONNECTION_STRING"] = connection;
        start.Environment["CATALOG_WORKER_TENANT"] = tenant;
        start.Environment["CATALOG_WORKER_INTERVAL_SECONDS"] = "1";
        return Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
    }
}
