using ConsumerRoot.Catalog.Contracts;
using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ConsumerRoot.Catalog;

internal sealed class CatalogQueries(
    CatalogDbContext database,
    IActorContextAccessor actor,
    ITenantContextAccessor tenancy
) : ICatalogQueries
{
    public async global::System.Threading.Tasks.Task<IReadOnlyList<CatalogItem>> ListAsync(
        CancellationToken cancellationToken
    )
    {
        // Presence invariants only. Authentication, admission and permission precede invocation.
        actor.Current.RequireIdentifiedActor();
        tenancy.Current.RequireTenant();
        return await database
            .Items.AsNoTracking()
            .OrderBy(row => row.Name)
            .Select(row => new CatalogItem(row.Id, row.Name))
            .ToListAsync(cancellationToken);
    }
}
