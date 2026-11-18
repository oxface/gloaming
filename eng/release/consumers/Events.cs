using System.Text.Json;
using Gloaming.Events.History;
using Gloaming.Events.Serialization;

var codec = new JsonEventCodec<Adjustment>(
    new JsonSerializerOptions(JsonSerializerDefaults.Web),
    [EventRegistration<Adjustment>.For<Adjusted>("stock.adjusted", 1)]
);
using var retained = JsonDocument.Parse("""{"quantity":7}""");
var decoded = (Adjusted)codec.Deserialize("stock.adjusted", 1, retained.RootElement);
Check(decoded.Quantity == 7, "Retained literal payload decoded.");
var encoded = codec.Serialize(new Adjusted(9));
Check(
    encoded.EventName == "stock.adjusted" && encoded.SchemaVersion == 1,
    "Stable durable identity."
);
Check(encoded.Payload.GetProperty("quantity").GetInt32() == 9, "Consumer JSON options preserved.");
Expect<EventDecodingException>(() => codec.Deserialize("unknown", 1, retained.RootElement));
Expect<EventDecodingException>(() => codec.Deserialize("stock.adjusted", 2, retained.RootElement));
var time = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
EventHistory.ValidateRange([new(1, time), new(2, time.AddSeconds(1))], 0, 2);
EventHistory.ValidateRange([], 2, 2);
Expect<EventHistoryException>(() => EventHistory.ValidateRange([new(1, time), new(3, time)], 0, 3));
Expect<EventHistoryException>(() =>
    EventHistory.ValidateRange([new(1, time), new(2, time.AddSeconds(-1))], 0, 2)
);
Expect<EventHistoryException>(() => EventHistory.ValidateRange([new(1, time)], 0, 2));
Console.WriteLine(
    "PASS events: retained JSON, durable alias, codec errors, contiguous history and time regression"
);

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Expect<T>(Action action)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

abstract record Adjustment;

sealed record Adjusted(int Quantity) : Adjustment;
