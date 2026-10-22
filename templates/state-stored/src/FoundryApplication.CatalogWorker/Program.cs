using ConsumerRoot.CatalogWorker;
using ConsumerRoot.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args is not ([] or ["--once"]))
{
    Console.Error.WriteLine(
        "Supported worker command: --once (or no arguments for periodic reporting)."
    );
    return 2;
}

string? connection = Environment.GetEnvironmentVariable("CATALOG_CONNECTION_STRING");
string? tenant = Environment.GetEnvironmentVariable("CATALOG_WORKER_TENANT");
if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(tenant))
{
    Console.Error.WriteLine(
        "Worker requires CATALOG_CONNECTION_STRING and CATALOG_WORKER_TENANT for its authorized deployment mandate."
    );
    return 2;
}

if (
    !int.TryParse(
        Environment.GetEnvironmentVariable("CATALOG_WORKER_INTERVAL_SECONDS") ?? "30",
        out int seconds
    ) || seconds is < 1 or > 3600
)
{
    Console.Error.WriteLine("CATALOG_WORKER_INTERVAL_SECONDS must be from 1 to 3600.");
    return 2;
}

var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
Composition.AddServices(builder.Services, connection);
var outcome = new WorkerOutcome();
builder.Services.AddSingleton(outcome);
builder.Services.AddSingleton(
    new WorkerSettings(tenant, args.Length == 1, TimeSpan.FromSeconds(seconds))
);
builder.Services.AddHostedService<CatalogReportWorker>();
using var host = builder.Build();
await host.RunAsync();
return outcome.Failed ? 1 : 0;
