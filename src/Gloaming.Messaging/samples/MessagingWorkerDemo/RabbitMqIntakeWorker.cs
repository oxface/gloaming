using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModulithFoundry.Samples.MessagingDemo;

namespace ModulithFoundry.Samples.MessagingWorkerDemo;

internal sealed partial class RabbitMqIntakeWorker(
    BrokerSession broker,
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<RabbitMqIntakeWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var receiver = new RabbitMqRenderReceiver(broker.Channel, scopes);
            while (!stoppingToken.IsCancellationRequested)
            {
                var delivery = await broker.Channel.BasicGetAsync(
                    broker.Queue,
                    autoAck: false,
                    stoppingToken
                );
                if (delivery is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
                    continue;
                }

                // The existing adapter commits retained intake before acknowledging this delivery.
                var result = await receiver.ReceiveAsync(delivery, stoppingToken);
                DeliveryAccepted(logger, delivery.BasicProperties.MessageId, result);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception failure)
        {
            // Failed/malformed intake is not acknowledged or silently discarded. Closing this
            // process's channel requeues it; a supervisor/operator owns transport restart policy.
            IntakeFailed(logger, failure);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Delivery {MessageId} retained and acknowledged: {Result}."
    )]
    private static partial void DeliveryAccepted(
        ILogger logger,
        string? messageId,
        InboxReceiveResult result
    );

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Intake stopped. Unacknowledged work remains recoverable after channel closure."
    )]
    private static partial void IntakeFailed(ILogger logger, Exception failure);
}
