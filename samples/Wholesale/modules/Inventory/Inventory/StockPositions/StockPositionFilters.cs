namespace ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

internal static class StockPositionFilters
{
    internal static IQueryable<StockPositionStateRow> WithAvailableAtLeast(
        this IQueryable<StockPositionStateRow> rows,
        decimal quantity
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        // Older inline JSON lacks reserved; PostgreSQL yields NULL, so treat it as zero.
        return rows.Where(row =>
            row.State.GetProperty("onHand").GetDecimal()
                - ((decimal?)row.State.GetProperty("reserved").GetDecimal() ?? 0m)
            >= quantity
        );
    }
}
