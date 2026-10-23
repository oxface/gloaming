using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Inventory;
using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo;

internal static class HistoryJourneys
{
    internal static void Run(string fixtureDirectory, TextWriter output)
    {
        const long capturedHead = 3;
        var stockRows = StockPositionHistoryExample.Load(fixtureDirectory);
        var stockCodec = StockPositionExample.CreateCodec();
        var currentStock = StockPositionHistoryExample.ReadAtVersion(
            stockCodec,
            stockRows,
            capturedHead,
            3
        )!;
        var earlierStock = StockPositionHistoryExample.ReadAtVersion(
            stockCodec,
            stockRows,
            capturedHead,
            2
        )!;
        var cutoffStock = StockPositionHistoryExample.ReadAsOf(
            stockCodec,
            stockRows,
            capturedHead,
            StockPositionHistoryExample.FirstReceiptAt
        )!;
        var beforeStock = StockPositionHistoryExample.ReadAsOf(
            stockCodec,
            stockRows,
            capturedHead,
            StockPositionHistoryExample.OpenedAt.AddTicks(-1)
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"inventory history: head={capturedHead}, current={currentStock.OnHand:F3}, at-version-2={earlierStock.OnHand:F3}, at-cutoff={cutoffStock.OnHand:F3}, before-open={(beforeStock is null ? "none" : "present")}"
            )
        );

        var orderRows = PurchaseOrderHistoryExample.Load(fixtureDirectory);
        var orderCodec = PurchaseOrderExample.CreateCodec();
        var currentOrder = PurchaseOrderHistoryExample.ReadAtVersion(
            orderCodec,
            orderRows,
            capturedHead,
            3
        )!;
        var earlierOrder = PurchaseOrderHistoryExample.ReadAtVersion(
            orderCodec,
            orderRows,
            capturedHead,
            2
        )!;
        var cutoffOrder = PurchaseOrderHistoryExample.ReadAsOf(
            orderCodec,
            orderRows,
            capturedHead,
            PurchaseOrderHistoryExample.LinesRecordedAt
        )!;
        var beforeOrder = PurchaseOrderHistoryExample.ReadAsOf(
            orderCodec,
            orderRows,
            capturedHead,
            PurchaseOrderHistoryExample.DraftedAt.AddTicks(-1)
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"purchasing history: head={capturedHead}, current-total={currentOrder.Total:F2}, at-version-2={earlierOrder.Total:F2}, at-cutoff={cutoffOrder.Total:F2}, before-draft={(beforeOrder is null ? "none" : "present")}"
            )
        );
    }
}
