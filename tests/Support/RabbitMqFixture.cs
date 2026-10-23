using Testcontainers.RabbitMq;

namespace Gloaming.Tests.Infrastructure;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer broker = new RabbitMqBuilder(
        "rabbitmq:4.3.6-management"
    ).Build();
    public string ConnectionString => broker.GetConnectionString();

    public async ValueTask InitializeAsync() => await broker.StartAsync();

    public async ValueTask DisposeAsync() => await broker.DisposeAsync();
}
