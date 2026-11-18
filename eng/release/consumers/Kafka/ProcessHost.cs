using System.Diagnostics;
using System.Reflection;

sealed class ChildHost : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> output;
    private readonly Task<string> errors;
    private readonly TaskCompletionSource<long> barrier = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private ChildHost(Process child)
    {
        process = child;
        output = DrainAsync();
        errors = process.StandardError.ReadToEndAsync();
    }

    internal static ChildHost Start(params string[] args)
    {
        var info = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"
        )
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        return new ChildHost(
            Process.Start(info) ?? throw new InvalidOperationException("Cannot start child.")
        );
    }

    private async Task<string> DrainAsync()
    {
        var lines = new List<string>();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            lines.Add(line);
            if (line.StartsWith("BARRIER:", StringComparison.Ordinal))
                barrier.TrySetResult(
                    long.Parse(line[8..], System.Globalization.CultureInfo.InvariantCulture)
                );
        }

        if (!barrier.Task.IsCompleted)
            barrier.TrySetException(
                new InvalidOperationException(
                    "Child exited without barrier: " + string.Join('\n', lines)
                )
            );

        return string.Join('\n', lines);
    }

    internal async Task<long> BarrierAsync(CancellationToken token) =>
        await barrier.Task.WaitAsync(TimeSpan.FromSeconds(45), token);

    internal async Task ResumeAsync()
    {
        await process.StandardInput.WriteLineAsync("continue");
        await process.StandardInput.FlushAsync();
    }

    internal async Task<string> FinishAsync(CancellationToken token)
    {
        await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(20), token);
        var combined = await output + "\n" + await errors;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Child failed: " + combined);

        return combined;
    }

    internal async Task KillAsync()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(cleanup.Token);
        await Task.WhenAll(output, errors).WaitAsync(cleanup.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await KillAsync();
        process.Dispose();
    }
}
