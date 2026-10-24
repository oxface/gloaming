using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gloaming.Samples.Wholesale.Worker.Broker;

internal sealed partial class WorkflowIntakeWorker(
    WorkflowBroker broker,
    IServiceScopeFactory scopes,
    ILogger<WorkflowIntakeWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool command = await broker.ReceiveCommandAsync(scopes, stoppingToken);
                bool reply = await broker.ReceiveReplyAsync(scopes, stoppingToken);
                bool reservation = await broker.ReceiveReservationAsync(scopes, stoppingToken);
                bool release = await broker.ReceiveReleaseAsync(scopes, stoppingToken);
                bool reservationReply = await broker.ReceiveReservationReplyAsync(
                    scopes,
                    stoppingToken
                );
                if (!command && !reply && !reservation && !release && !reservationReply)
                    await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                IntakeFailed(logger, failure);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Workflow intake failed; polling will continue."
    )]
    private static partial void IntakeFailed(ILogger logger, Exception failure);
}

internal sealed partial class StockIssueDeadlineWorker(
    IServiceScopeFactory scopes,
    ILogger<StockIssueDeadlineWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Explicit finite sample admission. Scheduling and tenant enumeration are consumer policy.
            foreach (string organization in new[] { "wholesale-alpha", "wholesale-beta" })
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    scope
                        .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                        .Initialize(TenantContext.ForTenant(new TenantId(organization)));
                    await scope
                        .ServiceProvider.GetRequiredService<StockIssueDeadlines>()
                        .MarkOverdueAsync(64, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception failure)
                {
                    ScanFailed(logger, failure);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Sales deadline scan failed; retry uses a fresh scope."
    )]
    private static partial void ScanFailed(ILogger logger, Exception failure);
}
