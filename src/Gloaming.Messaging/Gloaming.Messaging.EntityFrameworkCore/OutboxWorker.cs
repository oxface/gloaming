using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.EntityFrameworkCore;

internal sealed partial class OutboxWorker<TDbContext>(
    IServiceScopeFactory scopes,
    ILogger<OutboxWorker<TDbContext>> logger,
    OutboxWorkerOptions options
) : BackgroundService
    where TDbContext : DbContext
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve once before the loop: unsupported model/provider and missing bindings fail startup.
        await using (var scope = scopes.CreateAsyncScope())
            _ = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher<TDbContext>>();
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            await using (var scope = scopes.CreateAsyncScope())
            {
                // Resolve outside the retry catch so dependency/configuration failures remain visible.
                var dispatcher = scope.ServiceProvider.GetRequiredService<
                    IOutboxDispatcher<TDbContext>
                >();
                try
                {
                    var result = await dispatcher.DispatchNextAsync(stoppingToken);
                    delay =
                        result == OutboxDispatchResult.NoWork ? options.IdleDelay : TimeSpan.Zero;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OutboxFailureAccountingException failure)
                {
                    if (stoppingToken.IsCancellationRequested)
                        return;

                    AccountingNotConfirmed(logger, failure, failure.MessageId);
                    throw;
                }
                catch (Exception failure)
                {
                    DispatchFailed(logger, failure);
                    delay = options.FailureDelay;
                }
            }
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Outbox failure accounting was not confirmed for {MessageId}; stopping the worker."
    )]
    private static partial void AccountingNotConfirmed(
        ILogger logger,
        Exception exception,
        Guid messageId
    );

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Outbox dispatch failed; acceptance may already have occurred. Retrying in a fresh scope."
    )]
    private static partial void DispatchFailed(ILogger logger, Exception exception);
}
