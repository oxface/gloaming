using ConsumerRoot.Catalog.Contracts;
using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ConsumerRoot.Catalog;

internal sealed class CatalogCommands(
    CatalogDbContext database,
    IActorContextAccessor actor,
    ITenantContextAccessor tenancy
) : ICatalogCommands
{
    public global::System.Threading.Tasks.Task<CatalogChange> CreateAsync(
        Guid id,
        string name,
        decimal price,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        string tenant = RequireWriteScope();
        var candidate = CatalogItemState.Create(name, price);
        database.Items.Add(
            new ItemRow
            {
                Id = id,
                TenantKey = tenant,
                Name = candidate.Name,
                Price = candidate.Price,
                Version = 1,
            }
        );
        return global::System.Threading.Tasks.Task.FromResult<CatalogChange>(
            new CatalogChange.Changed(new(id, candidate.Name, candidate.Price, 1))
        );
    }

    public async global::System.Threading.Tasks.Task<CatalogChange> ChangeAsync(
        Guid id,
        long expectedVersion,
        string name,
        decimal price,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 1);

        _ = RequireWriteScope();
        var candidate = CatalogItemState.Create(name, price);
        var row = await database.Items.SingleOrDefaultAsync(
            value => value.Id == id,
            cancellationToken
        );
        if (row is null)
            return new CatalogChange.NotFound();
        if (row.Version != expectedVersion)
            return new CatalogChange.Conflict();

        long nextVersion = checked(row.Version + 1);
        row.Name = candidate.Name;
        row.Price = candidate.Price;
        row.Version = nextVersion;
        return new CatalogChange.Changed(new(row.Id, row.Name, row.Price, row.Version));
    }

    private string RequireWriteScope()
    {
        actor.Current.RequireIdentifiedActor();
        string tenant = tenancy.Current.RequireTenant().Value;
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Catalog commands require the caller's native transaction."
            );

        return tenant;
    }
}
