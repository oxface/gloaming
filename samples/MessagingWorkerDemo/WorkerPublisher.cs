using Gloaming.Messaging;
using Microsoft.Extensions.Logging;
using ModulithFoundry.Samples.MessagingDemo;

namespace ModulithFoundry.Samples.MessagingWorkerDemo;

internal sealed partial class WorkerPublisher(BrokerSession broker, ILogger<WorkerPublisher> logger)
    : IMessagePublisher
{
    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        await new RabbitMqExportPublisher(broker.Channel, broker.Queue).PublishAsync(
            message,
            cancellationToken
        );
        PublicationConfirmed(logger, message.MessageId);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Publication {MessageId} confirmed by the broker."
    )]
    private static partial void PublicationConfirmed(ILogger logger, Guid messageId);
}
