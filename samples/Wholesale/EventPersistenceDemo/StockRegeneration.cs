using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo;

// Trusted local host: selects policy and presentation; Gloaming executes regeneration.
public static class StockRegeneration
{
    public static ServiceCollection CreateServices(string connection)
    {
        var services = new ServiceCollection();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddInventoryDatabase(connection);
        services.AddStockPositionRebuilding();
        return services;
    }

    public static async Task<int> RunAsync(
        IServiceScopeFactory scopes,
        string organizationKey,
        Guid id,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(output);

        var progress = new JsonRegenerationProgress(output, organizationKey, id);
        var result = await StockPositionMaintenance.RegenerateAsync(
            scopes,
            organizationKey,
            id,
            new AggregateRegenerationOptions
            {
                MaxAttempts = 3,
                RetryDelay = TimeSpan.FromMilliseconds(100),
            },
            progress,
            cancellationToken
        );
        return progress.Complete(result);
    }

    // Inline reporting preserves ordered, flushed output; UI consumers can choose Progress<T> instead.
    private sealed class JsonRegenerationProgress(
        TextWriter output,
        string organizationKey,
        Guid id
    ) : IProgress<AggregateRegenerationProgress>
    {
        private readonly Guid invocation = Guid.NewGuid();

        public void Report(AggregateRegenerationProgress report) =>
            Write(
                report.Attempt,
                report.Phase switch
                {
                    AggregateRegenerationPhase.Started => "started",
                    AggregateRegenerationPhase.Replayed => "replayed",
                    AggregateRegenerationPhase.Retrying => "conflict",
                    _ => throw new InvalidDataException("Unknown regeneration phase."),
                },
                report.CapturedHead,
                report.Phase == AggregateRegenerationPhase.Retrying ? "writer-conflict" : null
            );

        internal int Complete(AggregateRegenerationResult result)
        {
            var (phase, exit) = result.Status switch
            {
                AggregateRegenerationStatus.Completed => ("completed", 0),
                AggregateRegenerationStatus.NotFound => ("not-found", 2),
                AggregateRegenerationStatus.Conflict => ("failed", 1),
                AggregateRegenerationStatus.Cancelled => ("cancelled", 130),
                AggregateRegenerationStatus.Failed => ("failed", 1),
                AggregateRegenerationStatus.Indeterminate => ("indeterminate", 4),
                _ => throw new InvalidDataException("Unknown regeneration outcome."),
            };
            // Presentation deliberately excludes provider messages and payload/connection details.
            string? reason =
                result.Status == AggregateRegenerationStatus.Conflict ? "writer-conflict"
                : result.Error is OperationCanceledException ? "cancellation"
                : result.Error?.GetType().Name;
            Write(result.Attempts, phase, result.CapturedHead, reason);
            return exit;
        }

        private void Write(int attempt, string phase, AggregateRebuildResult? head, string? reason)
        {
            output.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        invocation,
                        organizationKey,
                        stockPositionId = id,
                        attempt,
                        phase,
                        version = head?.Version,
                        reason,
                    }
                )
            );
            output.Flush();
        }
    }
}
