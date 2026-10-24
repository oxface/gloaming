using System.Text.Json;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

internal static class PostgresJsonValues
{
    // JSONB discards all but the last duplicate property. Reject ambiguous producer values
    // before staging so in-memory proposals, native stored evidence and equal retries agree.
    internal static void RequireUniqueProperties(JsonElement payload)
    {
        var pending = new Stack<JsonElement>();
        pending.Push(payload);
        while (pending.TryPop(out var value))
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new ArgumentException(
                            "Use JSON with unique object property names at every nesting level.",
                            nameof(payload)
                        );

                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        pending.Push(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        pending.Push(item);
            }
        }
    }
}
