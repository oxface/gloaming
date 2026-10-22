namespace ConsumerRoot.Catalog.Contracts;

// Changed is a staged proposal. The caller owns its native transaction, save and commit.
public interface ICatalogCommands
{
    global::System.Threading.Tasks.Task<CatalogChange> CreateAsync(
        Guid id,
        string name,
        decimal price,
        CancellationToken cancellationToken
    );
    global::System.Threading.Tasks.Task<CatalogChange> ChangeAsync(
        Guid id,
        long expectedVersion,
        string name,
        decimal price,
        CancellationToken cancellationToken
    );
}

public abstract record CatalogChange
{
    public sealed record Changed(CatalogItem Proposed) : CatalogChange;

    public sealed record NotFound : CatalogChange;

    public sealed record Conflict : CatalogChange;
}
