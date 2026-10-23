namespace Gloaming.Samples.Wholesale.Sales.DraftOrders;

internal static class DraftOrderFilters
{
    internal static IQueryable<DraftOrderReadView> WithCustomerNameContaining(
        this IQueryable<DraftOrderReadView> rows,
        string name
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return rows.Where(row => row.CustomerName.Contains(name));
    }

    internal static IQueryable<DraftOrderReadView> InCurrency(
        this IQueryable<DraftOrderReadView> rows,
        string currency
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return rows.Where(row => row.Currency == currency);
    }

    internal static IQueryable<DraftOrderReadView> WithTotalAtLeast(
        this IQueryable<DraftOrderReadView> rows,
        decimal minimum
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);

        return rows.Where(row => row.Total >= minimum);
    }
}
