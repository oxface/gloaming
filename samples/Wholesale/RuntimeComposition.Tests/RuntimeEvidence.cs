using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.RuntimeComposition.Tests;

internal sealed class RuntimeEvidence(DistributedApplication app, string scenario)
    : IAsyncDisposable
{
    private static readonly string[] Resources =
    [
        "postgres",
        "wholesale",
        "keycloak",
        "demo-setup",
        "api",
    ];
    private const int MaxLinesPerResource = 40;
    private const int MaxLineLength = 1024;

    public async ValueTask DisposeAsync()
    {
        string? directory = Environment.GetEnvironmentVariable("WHOLESALE_RUNTIME_RESULTS");
        if (string.IsNullOrEmpty(directory))
            return;

        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var logger = app.Services.GetRequiredService<ResourceLoggerService>();
            var evidence = new StringBuilder();
            foreach (string name in Resources)
            {
                if (!app.ResourceNotifications.TryGetCurrentState(name, out var resource))
                    continue;

                var snapshot = resource.Snapshot;
                evidence.AppendLine(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"[{name}] state={snapshot.State?.Text} health={snapshot.HealthStatus} exit={snapshot.ExitCode}"
                );
                var tail = new Queue<LogLine>(MaxLinesPerResource);
                await foreach (
                    var batch in logger
                        .GetAllAsync(resource.ResourceId)
                        .WithCancellation(deadline.Token)
                )
                {
                    foreach (var line in batch)
                    {
                        if (tail.Count == MaxLinesPerResource)
                            tail.Dequeue();
                        tail.Enqueue(line);
                    }
                }

                foreach (var line in tail)
                    evidence.AppendLine(
                        line.Content[..Math.Min(line.Content.Length, MaxLineLength)]
                    );
            }

            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, $"{scenario}-apphost.log"),
                evidence.ToString(),
                deadline.Token
            );
        }
        catch (Exception error)
            when (error
                    is IOException
                        or UnauthorizedAccessException
                        or OperationCanceledException
                        or InvalidOperationException
            )
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Could not save AppHost diagnostics: {error.Message}"
            );
        }
    }
}
