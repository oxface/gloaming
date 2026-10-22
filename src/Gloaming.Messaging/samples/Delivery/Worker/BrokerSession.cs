using Gloaming.Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace ModulithFoundry.Samples.Messaging.Worker;

// Queue/role selection stays in the consumer; the optional integration owns native lifetime.
internal sealed class BrokerSession(RabbitMqSession session, IChannel channel, string queue)
    : IAsyncDisposable
{
    internal IChannel Channel => channel;
    internal string Queue => queue;

    internal IDisposable Supervise(
        IHostApplicationLifetime lifetime,
        ILogger<BrokerSession> logger
    ) =>
        session.ObserveTerminalShutdown(
            lifetime.ApplicationStopping,
            _ =>
            {
                Environment.ExitCode = 1;
                lifetime.StopApplication();
            },
            logger
        );

    internal static async Task<BrokerSession> OpenAsync(
        IConfiguration configuration,
        bool createQueue,
        CancellationToken cancellation
    )
    {
        string queue =
            configuration["RabbitMQ:Queue"]
            ?? throw new InvalidOperationException("Configure RabbitMQ:Queue.");
        string address =
            configuration["RabbitMQ:Uri"]
            ?? throw new InvalidOperationException("Configure RabbitMQ:Uri.");
        var session = await RabbitMqSession.OpenAsync(
            factory =>
            {
                factory.Uri = new Uri(address);
                factory.AutomaticRecoveryEnabled = false;
                factory.ClientProvidedName = "messaging-worker-" + configuration["role"];
            },
            new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
            cancellation
        );
        try
        {
            var channel = await session.CreateChannelAsync(new(true, true), cancellation);
            if (createQueue)
                await channel.QueueDeclareAsync(
                    queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    cancellationToken: cancellation
                );
            else
                await channel.QueueDeclarePassiveAsync(queue, cancellation);

            return new(session, channel, queue);
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => session.DisposeAsync();
}
