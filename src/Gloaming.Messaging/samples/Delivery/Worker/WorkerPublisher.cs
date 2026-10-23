using Gloaming.Messaging;
using Gloaming.Samples.Messaging.Worker.Transport;
using Microsoft.Extensions.Logging;

namespace Gloaming.Samples.Messaging.Worker;

internal sealed partial class WorkerPublisher(BrokerSession broker, ILogger<WorkerPublisher> logger)
    : IMessagePublisher
{
    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        PublicationStarted(logger, message.MessageId);
        await new RabbitMqExportPublisher(broker.Channel, broker.Queue).PublishAsync(
            message,
            cancellationToken
        );
        PublicationConfirmed(logger, message.MessageId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Publication {MessageId} started.")]
    private static partial void PublicationStarted(ILogger logger, Guid messageId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Publication {MessageId} confirmed by the broker."
    )]
    private static partial void PublicationConfirmed(ILogger logger, Guid messageId);
}
