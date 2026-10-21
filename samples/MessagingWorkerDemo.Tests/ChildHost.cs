using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace ModulithFoundry.Samples.MessagingWorkerDemo.Tests;

// Launch the built executable, rather than an in-process approximation of its DI graph.
// This Linux CI harness only signals/kills processes it created itself.
internal sealed class ChildHost : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentQueue<string> output = new();
    private readonly Task[] drains;
    private readonly string[] secrets;

    internal ChildHost(
        Assembly executable,
        Dictionary<string, string> environment,
        params string[] arguments
    )
    {
        secrets = environment
            .Where(item =>
                item.Value.Contains("Password=", StringComparison.OrdinalIgnoreCase)
                || item.Value.StartsWith("amqp", StringComparison.Ordinal)
                || item.Key.EndsWith("_TOKEN", StringComparison.OrdinalIgnoreCase)
            )
            .Select(item => item.Value)
            .Where(value => value.Length > 0)
            .ToArray();
        var start = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"
        )
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(executable.Location);
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        // A role must not accidentally depend on the developer's ambient sample settings.
        foreach (
            string key in start
                .Environment.Keys.Where(key =>
                    key.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("RabbitMQ__", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("OUTBOX_DEMO_", StringComparison.OrdinalIgnoreCase)
                )
                .ToArray()
        )
            start.Environment.Remove(key);

        foreach (var (key, value) in environment)
            start.Environment[key] = value;

        start.Environment["Logging__LogLevel__Microsoft.EntityFrameworkCore"] = "Warning";
        start.Environment["Logging__Console__FormatterOptions__ColorBehavior"] = "Disabled";
        process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Child host did not start.");
        drains = [DrainAsync(process.StandardOutput), DrainAsync(process.StandardError)];
    }

    internal string Output => string.Join(Environment.NewLine, output);

    internal async Task<string> WaitForOutputAsync(string fragment, CancellationToken cancellation)
    {
        while (true)
        {
            string? line = output.FirstOrDefault(line =>
                line.Contains(fragment, StringComparison.Ordinal)
            );
            if (line is not null)
                return line;

            Assert.False(
                process.HasExited,
                $"Host exited before '{fragment}'.{Environment.NewLine}{Output}"
            );
            try
            {
                await Task.Delay(50, cancellation);
            }
            catch (OperationCanceledException failure) when (cancellation.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Host did not report '{fragment}'.{Environment.NewLine}{Output}",
                    failure
                );
            }
        }
    }

    internal async Task<int> WaitForExitAsync(CancellationToken cancellation)
    {
        await process.WaitForExitAsync(cancellation);
        await Task.WhenAll(drains);
        return process.ExitCode;
    }

    internal async Task StopAsync(CancellationToken cancellation)
    {
        if (!process.HasExited)
        {
            var signal = new ProcessStartInfo("kill") { UseShellExecute = false };
            signal.ArgumentList.Add("-TERM");
            signal.ArgumentList.Add(
                process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            using var sender = Process.Start(signal)!;
            await sender.WaitForExitAsync(cancellation);
            Assert.Equal(0, sender.ExitCode);
        }

        Assert.True(await WaitForExitAsync(cancellation) == 0, Output);
    }

    internal async Task KillAsync(CancellationToken cancellation)
    {
        process.Kill(entireProcessTree: true);
        await WaitForExitAsync(cancellation);
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);

        // Cleanup has a separate budget even when a proof's deadline was cancelled.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await WaitForExitAsync(cleanup.Token);
        }
        finally
        {
            process.Dispose();
        }
    }

    private async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            foreach (string secret in secrets)
                line = line.Replace(secret, "[connection redacted]", StringComparison.Ordinal);

            output.Enqueue(line);
        }
    }
}
