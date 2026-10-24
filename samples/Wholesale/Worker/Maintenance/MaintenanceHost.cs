using Gloaming.Samples.Wholesale.Worker.Maintenance;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.Worker.Maintenance;

internal static class MaintenanceHost
{
    internal static async Task RunAsync(string[] args)
    {
        if (args.FirstOrDefault() == "--regenerate-stock")
        {
            if (
                args.Length != 3
                || string.IsNullOrWhiteSpace(args[1])
                || !Guid.TryParse(args[2], out var id)
                || id == Guid.Empty
            )
            {
                Console.Error.WriteLine(
                    "Usage: --regenerate-stock <organization-key> <stock-position-id>"
                );
                Environment.ExitCode = 64;
                return;
            }

            string? maintenanceConnection = Environment.GetEnvironmentVariable(
                "WHOLESALE_DEMO_CONNECTION_STRING"
            );
            if (string.IsNullOrWhiteSpace(maintenanceConnection))
            {
                Console.Error.WriteLine(
                    "Set WHOLESALE_DEMO_CONNECTION_STRING to the existing Inventory database."
                );
                Environment.ExitCode = 64;
                return;
            }

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, signal) =>
            {
                signal.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                await using var maintenance = StockRegeneration
                    .CreateServices(maintenanceConnection)
                    .BuildServiceProvider(
                        new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
                    );
                Environment.ExitCode = await StockRegeneration.RunAsync(
                    maintenance.GetRequiredService<IServiceScopeFactory>(),
                    args[1],
                    id,
                    Console.Out,
                    cancellation.Token
                );
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }

            return;
        }
    }
}
