using System.Text.Json;
using Gloaming.Events.Serialization;

namespace Gloaming.Samples.Wholesale.EventCodecDemo;

// The caller owns parsing and envelope extraction; the codec sees only identity and payload.
internal static class FixtureEvents
{
    internal static IEnumerable<TEvent> Read<TEvent>(
        JsonEventCodec<TEvent> codec,
        params string[] paths
    )
        where TEvent : class
    {
        foreach (string path in paths)
        {
            SerializedEvent envelope = ReadEnvelope(path);
            yield return codec.Deserialize(
                envelope.EventName,
                envelope.SchemaVersion,
                envelope.Payload
            );
        }
    }

    internal static SerializedEvent ReadEnvelope(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement envelope = document.RootElement;
        return new SerializedEvent(
            envelope.GetProperty("eventName").GetString()!,
            envelope.GetProperty("schemaVersion").GetInt32(),
            envelope.GetProperty("payload").Clone()
        );
    }
}
