using System.Text.Json;
using System.Text.Json.Nodes;
using Gloaming.Events.Serialization;

namespace Gloaming.Samples.Wholesale.Inventory.StockPositions;

// Shape-only change: preserve the historical quantity and every unrelated field.
internal sealed class ReceiptV1ToV2() : JsonEventUpcaster("inventory.stock-position.received", 1, 2)
{
    public override JsonElement Upcast(JsonElement payload)
    {
        if (
            payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("quantity", out JsonElement quantity)
            || quantity.ValueKind != JsonValueKind.Number
            || !quantity.TryGetDecimal(out decimal value)
        )
            throw new JsonException("The v1 receipt requires a decimal quantity.");

        JsonObject upgraded = JsonNode.Parse(payload.GetRawText())!.AsObject();
        upgraded.Remove("quantity");
        upgraded["receivedQuantity"] = value;
        return JsonSerializer.SerializeToElement(upgraded);
    }
}
