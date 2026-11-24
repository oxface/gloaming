using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Gloaming.Samples.Wholesale.RuntimeComposition.Tests;

internal static class WholesaleContainerCleanup
{
    internal const string OwnerLabel = "io.gloaming.wholesale.test-owner";
    private const string MachineIdPath = "/etc/machine-id";
    private const string BootIdPath = "/proc/sys/kernel/random/boot_id";
    private const string DcpLabel = "com.microsoft.developer.usvc-dev";
    private static readonly string ListTemplate = string.Join(
        '\t',
        "{{.ID}}",
        LabelColumn(OwnerLabel),
        LabelColumn(DcpLabel + ".creatorProcessId"),
        LabelColumn(DcpLabel + ".creatorProcessStartTime"),
        LabelColumn(DcpLabel + ".persistent")
    );

    private static string LabelColumn(string name) => "{{index .Labels \"" + name + "\"}}";

    internal static void Configure(IDistributedApplicationBuilder builder)
    {
        builder.Configuration["DcpPublisher:WaitForResourceCleanup"] = "true";
        string? runtime = Environment.GetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME");
        string? endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST");
        bool podman = runtime is null
            ? endpoint is not null
                && endpoint.StartsWith("unix://", StringComparison.Ordinal)
                && endpoint.EndsWith("/podman.sock", StringComparison.Ordinal)
            : string.Equals(runtime, "podman", StringComparison.OrdinalIgnoreCase);
        if (
            !OperatingSystem.IsLinux()
            || !podman
            || !File.Exists(MachineIdPath)
            || !File.Exists(BootIdPath)
        )
            return;

        builder.Eventing.Subscribe<BeforeStartEvent>(
            (starting, cancellation) => PrepareAsync(starting.Model.Resources, cancellation)
        );
    }

    private static async Task PrepareAsync(
        IEnumerable<IResource> resources,
        CancellationToken cancellation
    )
    {
        string machine = Convert.ToHexStringLower(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    (await File.ReadAllTextAsync(MachineIdPath, cancellation)).Trim()
                )
            )
        );
        var boot = Guid.Parse((await File.ReadAllTextAsync(BootIdPath, cancellation)).Trim());
        string listed = await RunPodmanAsync(
            ["ps", "--all", "--no-trunc", "--format", ListTemplate],
            cancellation
        );
        string[] abandoned = FindAbandoned(listed.Split('\n'), machine, boot, IsRunning).ToArray();
        if (abandoned.Length > 0)
        {
            await RunPodmanAsync(
                ["rm", "--ignore", "--force", "--volumes", .. abandoned],
                cancellation
            );
        }

        string owner = FormattableString.Invariant(
            $"{OwnerLabel}={machine}/{boot:D}/{Environment.ProcessId}"
        );
        foreach (var resource in resources.Where(resource => resource.IsContainer()))
        {
            resource.Annotations.Add(
                new ContainerRuntimeArgsCallbackAnnotation(args =>
                {
                    args.Add("--label");
                    args.Add(owner);
                })
            );
        }
    }

    internal static IEnumerable<string> FindAbandoned(
        IEnumerable<string> lines,
        string machine,
        Guid boot,
        Func<int, bool> isRunning
    )
    {
        var sessions = lines
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(fields =>
                fields.Length == 5
                && fields[0].Length == 64
                && fields[0].All(char.IsAsciiHexDigit)
                && fields[3].Length > 0
                && fields[4] == "false"
            )
            .GroupBy(fields => (Creator: fields[2], Started: fields[3]));
        foreach (var session in sessions)
        {
            // Use the exact DCP process identity to recover its generated tunnel too.
            string[] owners = session
                .Select(fields => fields[1])
                .Where(label => label.Length > 0)
                .Distinct()
                .ToArray();
            if (owners.Length != 1 || !TryGetPid(session.Key.Creator, out int creator))
                continue;

            string[] owner = owners[0].Split('/');
            if (
                owner.Length != 3
                || owner[0] != machine
                || !Guid.TryParseExact(owner[1], "D", out var ownerBoot)
                || !TryGetPid(owner[2], out int pid)
                || (ownerBoot == boot && (isRunning(pid) || isRunning(creator)))
            )
                continue;

            foreach (string[] container in session)
                yield return container[0];
        }
    }

    private static bool TryGetPid(string value, out int pid) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0;

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<string> RunPodmanAsync(
        string[] arguments,
        CancellationToken cancellation
    )
    {
        var start = new ProcessStartInfo("podman")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Inspect the local engine, where the test owner's process ID is meaningful.
        start.ArgumentList.Add("--remote=false");
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using var process =
            Process.Start(start) ?? throw new InvalidOperationException("Podman did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill();

            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Podman {arguments[0]} failed during Wholesale cleanup: {await error}"
            );

        return await output;
    }
}
