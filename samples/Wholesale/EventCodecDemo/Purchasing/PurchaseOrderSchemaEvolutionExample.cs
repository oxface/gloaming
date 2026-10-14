using System.Text.Json;
using Gloaming.Events.Serialization;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

// This independent adopter changes a flat event into a nested fact through two declared steps.
internal static class PurchaseOrderSchemaEvolutionExample
{
    internal const string LineEventName = "purchasing.purchase-order.line-set";

    internal static JsonEventCodec<IPurchaseOrderEvent> CreateCodec() =>
        new(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                RespectNullableAnnotations = true,
                RespectRequiredConstructorParameters = true,
            },
            [
                EventRegistration<IPurchaseOrderEvent>.For<PurchaseOrderDrafted>(
                    "purchasing.purchase-order.drafted",
                    1
                ),
                EventRegistration<IPurchaseOrderEvent>.For<NestedPurchaseOrderLineSet>(
                    LineEventName,
                    3
                ),
            ],
            [new LineV1ToV2(), new LineV2ToV3()]
        );

    internal static PurchaseOrderSummary Read(IEnumerable<IPurchaseOrderEvent> events) =>
        PurchaseOrderExample.Read(
            events.Select(@event =>
                @event is NestedPurchaseOrderLineSet changed
                    ? new PurchaseOrderLineSet(
                        changed.Line.ItemCode,
                        changed.Line.Quantity,
                        changed.Line.UnitPrice
                    )
                    : @event
            )
        );

    private sealed class LineV1ToV2() : JsonEventUpcaster(LineEventName, 1, 2)
    {
        public override JsonElement Upcast(JsonElement payload) =>
            JsonSerializer.SerializeToElement(
                new
                {
                    sku = ReadString(payload, "itemCode"),
                    units = ReadDecimal(payload, "quantity"),
                    pricePerUnit = ReadDecimal(payload, "unitPrice"),
                }
            );
    }

    private sealed class LineV2ToV3() : JsonEventUpcaster(LineEventName, 2, 3)
    {
        public override JsonElement Upcast(JsonElement payload) =>
            JsonSerializer.SerializeToElement(
                new
                {
                    line = new
                    {
                        itemCode = ReadString(payload, "sku"),
                        quantity = ReadDecimal(payload, "units"),
                        unitPrice = ReadDecimal(payload, "pricePerUnit"),
                    },
                }
            );
    }

    private static string ReadString(JsonElement payload, string name)
    {
        if (
            payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(name, out JsonElement field)
            || field.ValueKind != JsonValueKind.String
        )
            throw new JsonException("The historical line requires a string item code.");
        return field.GetString()!;
    }

    private static decimal ReadDecimal(JsonElement payload, string name)
    {
        if (
            payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(name, out JsonElement field)
            || field.ValueKind != JsonValueKind.Number
            || !field.TryGetDecimal(out decimal value)
        )
            throw new JsonException("The historical line requires a decimal quantity and price.");
        return value;
    }
}

internal sealed record NestedPurchaseOrderLineSet(PurchaseOrderLineDetails Line)
    : IPurchaseOrderEvent;

internal sealed record PurchaseOrderLineDetails(
    string ItemCode,
    decimal Quantity,
    decimal UnitPrice
);
