using System.Text.Json;
using System.Text.Json.Nodes;
using Gloaming.Events.Serialization;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;

// Durable literals remain in the codec family’s owned fixtures. Metadata is authored here.
public static class FixtureHistories
{
    public static readonly Guid StreamId = Guid.Parse("e5060000-0000-0000-0000-000000000001");
    public static readonly DateTimeOffset OpenedAt = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset FirstChangeAt = OpenedAt.AddSeconds(10);
    public static readonly DateTimeOffset LastStockChangeAt = OpenedAt.AddSeconds(20);

    public static (SerializedEvent Event, DateTimeOffset RecordedAt)[] Inventory(
        string directory,
        decimal multiplier = 1
    ) =>
        [
            (Read(directory, "Inventory/opened.v1.json", multiplier), OpenedAt),
            (Read(directory, "Inventory/received.v1.json", multiplier), FirstChangeAt),
            (Read(directory, "Inventory/received-second.v1.json", multiplier), LastStockChangeAt),
        ];

    private static SerializedEvent Read(string directory, string path, decimal multiplier)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, path)));
        JsonElement envelope = document.RootElement;
        JsonElement payload = envelope.GetProperty("payload").Clone();
        if (multiplier != 1 && payload.TryGetProperty("quantity", out var quantity))
        {
            var modified = JsonNode.Parse(payload.GetRawText())!;
            modified["quantity"] = quantity.GetDecimal() * multiplier;
            payload = JsonSerializer.SerializeToElement(modified);
        }
        return new SerializedEvent(
            envelope.GetProperty("eventName").GetString()!,
            envelope.GetProperty("schemaVersion").GetInt32(),
            payload
        );
    }
}
