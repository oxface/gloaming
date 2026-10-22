namespace ConsumerRoot.Catalog;

// Consumer-owned rule: a listed price is between zero and 10,000 inclusive.
// Construct the complete immutable candidate before mutating persistence records.
internal sealed record CatalogItemState
{
    private CatalogItemState(string name, decimal price) => (Name, Price) = (name, price);

    internal string Name { get; }
    internal decimal Price { get; }

    internal static CatalogItemState Create(string name, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, 200);
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(price, 10_000m);
        if (decimal.Round(price, 2) != price)
            throw new ArgumentException(
                "Catalog prices use at most two decimal places.",
                nameof(price)
            );

        return new(name, price);
    }
}
