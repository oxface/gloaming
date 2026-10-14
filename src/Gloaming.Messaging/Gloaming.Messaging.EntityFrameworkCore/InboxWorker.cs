using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.EntityFrameworkCore;

internal sealed partial class InboxWorker<TDbContext>(
    IServiceScopeFactory scopes,
    ILogger<InboxWorker<TDbContext>> logger,
    string subscriptionKey,
    InboxWorkerOptions options
) : BackgroundService
    where TDbContext : DbContext
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Validate processor/model and subscription binding before starting the background loop.
        // This scope is discarded; every processing attempt needs its own context and handler.
        await using (var scope = scopes.CreateAsyncScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<IInboxProcessor<TDbContext>>();
            _ = scope.ServiceProvider.GetRequiredKeyedService<IInboxHandler<TDbContext>>(
                subscriptionKey
            );
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<
                    IInboxProcessor<TDbContext>
                >();
                try
                {
                    var result = await processor.ProcessNextAsync(subscriptionKey, stoppingToken);
                    delay =
                        result == InboxProcessingResult.NoWork ? options.IdleDelay : TimeSpan.Zero;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception failure)
                {
                    ProcessingFailed(logger, failure, subscriptionKey);
                    delay = options.FailureDelay;
                }
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Inbox processing failed for {SubscriptionKey}; retrying in a fresh scope."
    )]
    private static partial void ProcessingFailed(
        ILogger logger,
        Exception exception,
        string subscriptionKey
    );
}
