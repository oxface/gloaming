namespace ConsumerRoot.Catalog;

internal static class CatalogFilters
{
    internal static IQueryable<ItemRow> Matching(
        this IQueryable<ItemRow> rows,
        string? name,
        decimal? minimumPrice
    )
    {
        if (name is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            rows = rows.Where(row => row.Name.Contains(name));
        }

        if (minimumPrice is { } minimum)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(minimum);

            rows = rows.Where(row => row.Price >= minimum);
        }

        return rows;
    }
}
