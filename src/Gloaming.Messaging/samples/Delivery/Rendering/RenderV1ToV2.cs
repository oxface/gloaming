using System.Text.Json;
using Gloaming.Events.Serialization;

namespace ModulithFoundry.Samples.Messaging.Rendering;

// Receiver-owned interpretation of the original wire contract; retained envelopes stay unchanged.
internal sealed class RenderV1ToV2() : JsonEventUpcaster("exports.render", 1, 2)
{
    private sealed record LegacyCommand(Guid ExportRequestId, int Pages);

    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
    };

    public override JsonElement Upcast(JsonElement payload)
    {
        var legacy =
            payload.Deserialize<LegacyCommand>(Json)
            ?? throw new JsonException("A render command is required.");
        return JsonSerializer.SerializeToElement(
            new { legacy.ExportRequestId, PageCount = legacy.Pages }
        );
    }
}
