namespace ConsumerRoot.Catalog.Contracts;

// Catalog owns tenant-scoped item names. This is editable consumer business vocabulary.
public sealed record CatalogItem(Guid Id, string Name, decimal Price = 0, long Version = 1);

public sealed record SearchCatalog(
    string? NameContains = null,
    decimal? MinimumPrice = null,
    int Offset = 0,
    int Limit = 20
);

public sealed record CatalogPage(IReadOnlyList<CatalogItem> Items, int TotalCount);

public interface ICatalogQueries
{
    global::System.Threading.Tasks.Task<IReadOnlyList<CatalogItem>> ListAsync(
        CancellationToken cancellationToken
    );

    global::System.Threading.Tasks.Task<CatalogPage> SearchAsync(
        SearchCatalog request,
        CancellationToken cancellationToken
    );
}
