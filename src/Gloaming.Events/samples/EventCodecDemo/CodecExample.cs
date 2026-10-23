using System.Text.Json;
using Gloaming.Events.Serialization;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo;

public static class CodecExample
{
    private const string ReceiptName = "inventory.stock-position.received";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
    };

    public static void Run(string fixtureDirectory, TextWriter output)
    {
        var codec = new JsonEventCodec<Receipt>(
            Json,
            [EventRegistration<Receipt>.For<Receipt>(ReceiptName, 2)],
            [new ReceiptV1ToV2()]
        );
        var earlier = Read("received.v1.json");
        var current = Read("received.v2.json");
        var stored = codec.Serialize(earlier);
        output.WriteLine(
            FormattableString.Invariant(
                $"receipt: v1={earlier.ReceivedQuantity}, v2={current.ReceivedQuantity}, write-schema={stored.SchemaVersion}"
            )
        );

        Receipt Read(string file)
        {
            var envelope = FixtureEvents.ReadEnvelope(
                Path.Combine(fixtureDirectory, "Inventory", file)
            );
            return codec.Deserialize(envelope.EventName, envelope.SchemaVersion, envelope.Payload);
        }
    }

    private sealed record Receipt(decimal ReceivedQuantity);

    // This field rename is consumer policy. Upcasting never rewrites the original envelope.
    private sealed class ReceiptV1ToV2() : JsonEventUpcaster(ReceiptName, 1, 2)
    {
        public override JsonElement Upcast(JsonElement payload)
        {
            if (
                payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("quantity", out var quantity)
                || quantity.ValueKind != JsonValueKind.Number
                || !quantity.TryGetDecimal(out decimal value)
            )
                throw new JsonException("A receipt requires a decimal quantity.");

            return JsonSerializer.SerializeToElement(new { receivedQuantity = value }, Json);
        }
    }
}
