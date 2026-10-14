using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.InboxDemo;
using ModulithFoundry.Samples.OutboxDemo;
using RabbitMQ.Client;

namespace ModulithFoundry.Samples.MessagingDemo;

public static class MessagingJourney
{
    public static async Task RunAsync(
        string senderConnection,
        string receiverConnection,
        Uri broker,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        Guid export = Guid.NewGuid();
        await using (var sender = ExportDbContext.Create(senderConnection))
        {
            // Finite executable setup, never worker/library migration behavior.
            await sender.Database.MigrateAsync(cancellationToken);
            sender.Add(ExportRequest.Create(export, 3));
            await sender.SaveChangesAsync(cancellationToken);
            await using var transaction = await sender.Database.BeginTransactionAsync(
                cancellationToken
            );
            var commands = new ExportRequestCommands(sender, new EfOutbox<ExportDbContext>(sender));
            if (
                await commands.SubmitAsync(export, 1, cancellationToken)
                != ExportSubmissionResult.Accepted
            )
                throw new InvalidOperationException("The export draft was not accepted.");
            await sender.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await using (var receiver = RenderDbContext.Create(receiverConnection))
            await receiver.Database.MigrateAsync(cancellationToken);
        var services = new ServiceCollection();
        InboxDemoHost.Register(services, receiverConnection);
        await using var provider = services.BuildServiceProvider();

        var factory = new ConnectionFactory { Uri = broker, AutomaticRecoveryEnabled = false };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            ),
            cancellationToken
        );
        string queue = "exports.render." + Guid.NewGuid().ToString("N");
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken
        );
        await using var dispatchContext = ExportDbContext.Create(senderConnection);
        var dispatched = await new PostgresOutboxDispatcher<ExportDbContext>(
            dispatchContext,
            new RabbitMqExportPublisher(channel, queue),
            new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1))
        ).DispatchNextAsync(cancellationToken);
        var delivery =
            await channel.BasicGetAsync(queue, autoAck: false, cancellationToken)
            ?? throw new InvalidOperationException("No confirmed export was available.");
        var received = await new RabbitMqRenderReceiver(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>()
        ).ReceiveAsync(delivery, cancellationToken);
        await using var processing = provider.CreateAsyncScope();
        var processed = await processing
            .ServiceProvider.GetRequiredService<IInboxProcessor<RenderDbContext>>()
            .ProcessNextAsync(RenderExportHandler.Subscription, cancellationToken);
        output.WriteLine(
            $"delivery {delivery.BasicProperties.MessageId}: outbox={dispatched}; inbox={received}; local job={processed}"
        );
        await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: true, cancellationToken);
    }
}
