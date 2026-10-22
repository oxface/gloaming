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
            .ThenBy(row => row.Id)
            .Select(row => new CatalogItem(row.Id, row.Name, row.Price, row.Version))
            .ToListAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<CatalogPage> SearchAsync(
        SearchCatalog request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.Offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Limit, 100);

        actor.Current.RequireIdentifiedActor();
        tenancy.Current.RequireTenant();
        var filtered = database
            .Items.AsNoTracking()
            .Matching(request.NameContains, request.MinimumPrice);
        int count = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderBy(row => row.Price)
            .ThenBy(row => row.Id)
            .Skip(request.Offset)
            .Take(request.Limit)
            .Select(row => new CatalogItem(row.Id, row.Name, row.Price, row.Version))
            .ToArrayAsync(cancellationToken);
        return new(items, count);
    }
}
