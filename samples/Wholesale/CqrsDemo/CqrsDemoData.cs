using System.Text.Json;
using Gloaming.Events.Serialization;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.CqrsDemo;

public static class CqrsDemoData
{
    public static Guid OrderId { get; } = Guid.Parse("c1000000-0000-0000-0000-000000000020");
    public static Guid LineId { get; } = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    public static Guid PositionId { get; } = Guid.Parse("c1000000-0000-0000-0000-000000000100");

    public static CreateDraftOrder Draft(Guid id, decimal total, string currency = "EUR") =>
        new(
            id,
            SalesDemoSeed.AlphaCustomerId,
            id.ToString("N"),
            currency,
            100,
            [new(LineId, "DEMO-NOTEBOOK", 1, total)]
        );

    // Finite setup, not a write protocol or integration feed.
    public static void StageStock(InventoryDbContext database)
    {
        var at = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        InventoryHistorySeed.Add(
            database,
            PositionId,
            [
                (
                    new SerializedEvent(
                        "inventory.stock-position.opened",
                        1,
                        JsonSerializer.SerializeToElement(
                            new
                            {
                                stockItemId = Guid.Parse("c1000000-0000-0000-0000-000000000101"),
                                stockingLocationId = Guid.Parse(
                                    "c1000000-0000-0000-0000-000000000102"
                                ),
                                baseUnitCode = "EA",
                            }
                        )
                    ),
                    at
                ),
                (
                    new SerializedEvent(
                        "inventory.stock-position.received",
                        2,
                        JsonSerializer.SerializeToElement(new { receivedQuantity = 10m })
                    ),
                    at
                ),
                (
                    new SerializedEvent(
                        "inventory.stock-position.reserved",
                        1,
                        JsonSerializer.SerializeToElement(
                            new
                            {
                                reservationId = Guid.Parse("c1000000-0000-0000-0000-000000000103"),
                                quantity = 7m,
                            }
                        )
                    ),
                    at
                ),
            ]
        );
    }
}
