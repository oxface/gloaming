using System.Text.Json;
using Testcontainers.RabbitMq;

namespace ModulithFoundry.Samples.Messaging.Tests.Processes;

// Dedicated to this sequential test class: native outages/alarms must not affect other proofs.
public sealed class BrokerRecoveryFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer broker = new RabbitMqBuilder(
        "rabbitmq:4.3.6-management"
    ).Build();

    internal string ConnectionString => broker.GetConnectionString();

    public async ValueTask InitializeAsync() => await broker.StartAsync();

    internal Task StopApplicationAsync(CancellationToken cancellation) =>
        ControlAsync(["rabbitmqctl", "stop_app"], cancellation);

    internal Task StartApplicationAsync(CancellationToken cancellation) =>
        ControlAsync(["rabbitmqctl", "start_app"], cancellation);

    internal Task BlockPublicationAsync(CancellationToken cancellation) =>
        ControlAsync(
            ["rabbitmqctl", "set_vm_memory_high_watermark", "absolute", "1"],
            cancellation
        );

    internal Task UnblockPublicationAsync(CancellationToken cancellation) =>
        ControlAsync(["rabbitmqctl", "set_vm_memory_high_watermark", "0.4"], cancellation);

    internal async Task<long> MessageCountAsync(string queue, CancellationToken cancellation)
    {
        string output = await ControlAsync(
            ["rabbitmqctl", "--quiet", "list_queues", "name", "messages", "--formatter", "json"],
            cancellation
        );
        using var document = JsonDocument.Parse(output);
        return document
            .RootElement.EnumerateArray()
            .Single(row => row.GetProperty("name").GetString() == queue)
            .GetProperty("messages")
            .GetInt64();
    }

    private async Task<string> ControlAsync(string[] command, CancellationToken cancellation)
    {
        var result = await broker.ExecAsync(command, cancellation);
        Assert.True(result.ExitCode == 0, result.Stdout + result.Stderr);
        return result.Stdout;
    }

    public async ValueTask DisposeAsync() => await broker.DisposeAsync();
}
