using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

internal static class StockPositionDecisions
{
    internal static decimal[] IssueQuantities(IssueStock request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfEqual(request.Id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.ExpectedVersion, 1);
        ArgumentNullException.ThrowIfNull(request.Issues);
        StockIssue[] items = request.Issues.ToArray();
        ArgumentOutOfRangeException.ThrowIfLessThan(items.Length, 1);
        return items
            .Select(item =>
            {
                ArgumentNullException.ThrowIfNull(item);
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(item.Quantity, 0);
                return item.Quantity;
            })
            .ToArray();
    }

    internal static (decimal Requested, IStockPositionEvent[]? Events) Issues(
        StockPositionState state,
        decimal[] quantities
    )
    {
        decimal requested = quantities.Aggregate(
            0m,
            (total, quantity) => checked(total + quantity)
        );
        // Whole-batch eligibility precedes fact production and candidate evolution.
        return (
            requested,
            requested > state.Available
                ? null
                : quantities
                    .Select(quantity => (IStockPositionEvent)new StockPositionIssued(quantity))
                    .ToArray()
        );
    }

    internal static void ValidateOpen(OpenStockPosition request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfEqual(request.Id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNotEqual(request.ExpectedVersion, 0);
        ArgumentOutOfRangeException.ThrowIfEqual(request.StockItemId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(request.StockingLocationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaseUnitCode);
    }

    internal static IStockPositionEvent[] Open(OpenStockPosition request)
    {
        ValidateOpen(request);
        return
        [
            new StockPositionOpened(
                request.StockItemId,
                request.StockingLocationId,
                request.BaseUnitCode
            ),
        ];
    }

    internal static StockReceipt[] ReceiptItems(ReceiveStock request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfEqual(request.Id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.ExpectedVersion, 1);
        ArgumentNullException.ThrowIfNull(request.Receipts);
        StockReceipt[] items = request.Receipts.ToArray();
        ArgumentOutOfRangeException.ThrowIfLessThan(items.Length, 1);
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(item.Quantity, 0);
        }
        return items;
    }

    internal static IStockPositionEvent[] Receipts(IReadOnlyList<StockReceipt> items) =>
        items
            .Select(item =>
            {
                ArgumentNullException.ThrowIfNull(item);
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(item.Quantity, 0);
                return (IStockPositionEvent)
                    new StockPositionReceived(item.Quantity, item.DeliveryReference);
            })
            .ToArray();
}
