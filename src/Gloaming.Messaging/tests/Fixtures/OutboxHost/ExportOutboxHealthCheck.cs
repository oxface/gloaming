using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using ModulithFoundry.Samples.Messaging.Exports;

namespace ModulithFoundry.Samples.Messaging.Tests.Fixtures.Outbox;

// This dedicated sample host has one dispatch BackgroundService when --worker is selected.
// Native task state is enough here; a larger host must explicitly identify its expected workers.
internal sealed class ExportOutboxHealthCheck(
    IServiceScopeFactory scopes,
    IServiceProvider services,
    IHostApplicationLifetime lifetime,
    bool workerExpected
) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (lifetime.ApplicationStopping.IsCancellationRequested)
            return HealthCheckResult.Unhealthy("The host is stopping.");

        if (workerExpected)
        {
            var workers = services
                .GetServices<IHostedService>()
                .OfType<BackgroundService>()
                .ToArray();
            if (workers.Length != 1 || workers[0].ExecuteTask is not { IsCompleted: false })
                return HealthCheckResult.Unhealthy("The expected dispatch worker is not running.");
        }

        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
        await database.ReadPostgresOutboxBacklogAsync("exports.render", cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
