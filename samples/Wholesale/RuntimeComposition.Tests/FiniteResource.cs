using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.RuntimeComposition.Tests;

internal static class FiniteResource
{
    public static async Task RunAsync(
        DistributedApplication app,
        string name,
        CancellationToken cancellation
    )
    {
        var commands = app.Services.GetRequiredService<ResourceCommandService>();
        var started = await commands.ExecuteCommandAsync(
            name,
            KnownResourceCommands.StartCommand,
            cancellation
        );
        Assert.True(started.Success, $"Could not start '{name}'. {started.Message}");
        var terminal = await app.ResourceNotifications.WaitForResourceAsync(
            name,
            resource =>
                resource.Snapshot.ExitCode.HasValue
                || resource.Snapshot.State?.Text == KnownResourceStates.FailedToStart,
            cancellation
        );
        string exitCode =
            terminal.Snapshot.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "missing";
        Assert.True(
            terminal.Snapshot.ExitCode == 0,
            $"Resource '{name}' ended in state '{terminal.Snapshot.State?.Text}' with exit code {exitCode}."
        );
    }
}
