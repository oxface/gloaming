using ConsumerRoot.Catalog.Contracts;
using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ConsumerRoot.CatalogWorker;

public static class WorkerMarker { }

internal sealed record WorkerSettings(string Tenant, bool Once, TimeSpan Interval);

internal sealed class WorkerOutcome
{
    internal bool Failed { get; set; }
}

internal sealed class CatalogReportWorker(
    IServiceScopeFactory scopes,
    WorkerSettings settings,
    IHostApplicationLifetime lifetime,
    WorkerOutcome outcome
) : BackgroundService
{
    protected override async global::System.Threading.Tasks.Task ExecuteAsync(
        CancellationToken stoppingToken
    )
    {
        try
        {
            await ReportAsync(stoppingToken);
        }
        catch (Exception) when (!stoppingToken.IsCancellationRequested)
        {
            outcome.Failed = true;
            throw;
        }
    }

    private async global::System.Threading.Tasks.Task ReportAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Interval);
        do
        {
            // Deployment configuration is a consumer-owned authorization mandate, not caller input.
            // Every iteration gets immutable contexts and a fresh, scoped DbContext.
            await using var scope = scopes.CreateAsyncScope();
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new(Actor.System(new("catalog-report-worker"))));
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(new(settings.Tenant)));
            var page = await scope
                .ServiceProvider.GetRequiredService<ICatalogQueries>()
                .SearchAsync(new(Limit: 100), stoppingToken);
            Console.WriteLine(
                $"Catalog report tenant={settings.Tenant} total={page.TotalCount} items={page.Items.Count}"
            );
            if (settings.Once)
            {
                lifetime.StopApplication();
                return;
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
