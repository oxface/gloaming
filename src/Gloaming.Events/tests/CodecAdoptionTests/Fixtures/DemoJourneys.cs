using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Inventory;
using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo;

public static class DemoJourneys
{
    public static void Run(string fixtureDirectory, TextWriter output)
    {
        var stock = StockPositionExample.Read(
            FixtureEvents.Read(
                StockPositionExample.CreateCodec(),
                Path.Combine(fixtureDirectory, "Inventory", "opened.v1.json"),
                Path.Combine(fixtureDirectory, "Inventory", "received.v1.json")
            )
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"inventory: item={stock.StockItemId}, location={stock.StockingLocationId}, unit={stock.BaseUnitCode}, on-hand={stock.OnHand}"
            )
        );
        var order = PurchaseOrderExample.Read(
            FixtureEvents.Read(
                PurchaseOrderExample.CreateCodec(),
                Path.Combine(fixtureDirectory, "Purchasing", "drafted.v1.json"),
                Path.Combine(fixtureDirectory, "Purchasing", "line-set.v1.json")
            )
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"purchasing: code={order.Code}, supplier={order.SupplierReference}, currency={order.Currency}, total={order.Total}"
            )
        );
        HistoryJourneys.Run(fixtureDirectory, output);
    }
}
