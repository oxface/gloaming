using Microsoft.Extensions.Configuration;
using ModulithFoundry.Samples.Wholesale.Worker.Broker;
using ModulithFoundry.Samples.Wholesale.Worker.Local;
using ModulithFoundry.Samples.Wholesale.Worker.Maintenance;

if (args.FirstOrDefault() == "--regenerate-stock")
{
    await MaintenanceHost.RunAsync(args);
    return;
}

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();
if (configuration["role"] == "inventory")
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    Environment.ExitCode = await BusinessOperations.RunAsync(configuration, deadline.Token);
    return;
}

switch (configuration["Transport"] ?? "broker")
{
    case "broker":
        await BrokerHost.RunAsync(args);
        break;
    case "local":
        await LocalHost.RunAsync(args);
        break;
    default:
        throw new InvalidOperationException("Choose --transport broker or local.");
}
