using Testcontainers.RabbitMq;

namespace ModulithFoundry.RabbitMqTests;

// Native outage controls are isolated from all other RabbitMQ proofs.
public sealed class RecoveryBrokerFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer broker = new RabbitMqBuilder(
        "rabbitmq:4.3.6-management"
    ).Build();
    internal string ConnectionString => broker.GetConnectionString();

    public async ValueTask InitializeAsync() => await broker.StartAsync();

    internal Task StopAsync(CancellationToken token) => ControlAsync("stop_app", token);

    internal Task StartAsync(CancellationToken token) => ControlAsync("start_app", token);

    private async Task ControlAsync(string command, CancellationToken token)
    {
        var result = await broker.ExecAsync(["rabbitmqctl", command], token);
        Assert.True(result.ExitCode == 0, result.Stdout + result.Stderr);
    }

    public async ValueTask DisposeAsync() => await broker.DisposeAsync();
}
