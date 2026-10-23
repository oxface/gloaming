using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.Inventory.StockPositions;

/// <summary>Trusted host entry for committed maintenance; Organization admission is caller-owned.</summary>
public static class StockPositionMaintenance
{
    public static Task<AggregateRegenerationResult> RegenerateAsync(
        IServiceScopeFactory scopes,
        string organizationKey,
        Guid id,
        AggregateRegenerationOptions options,
        IProgress<AggregateRegenerationProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var tenant = TenantContext.ForTenant(new TenantId(organizationKey));
        return new AggregateRegenerator<StockPositionAggregate, InventoryDbContext>(
            new OrganizationScopeFactory(scopes, tenant)
        ).RegenerateAsync(id, options, progress, cancellationToken);
    }

    // Admission is complete before construction. Every fresh native scope receives the same context.
    private sealed class OrganizationScopeFactory(IServiceScopeFactory scopes, TenantContext tenant)
        : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            var scope = scopes.CreateScope();
            try
            {
                scope
                    .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                    .Initialize(tenant);
                return scope;
            }
            catch (Exception failure)
            {
                // Only synchronous context-establishment services resolve before the scope is returned.
                try
                {
                    scope.Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(failure, cleanup);
                }

                throw;
            }
        }
    }
}
