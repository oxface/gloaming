using System.Text.Json;

namespace Gloaming.Events.Serialization;

/// <summary>Dispatches explicit durable event identities through consumer-configured native JSON.</summary>
public sealed class JsonEventCodec<TEvent>
    where TEvent : class
{
    private readonly Dictionary<Type, EventRegistration<TEvent>> byType = [];
    private readonly Dictionary<(string Name, int Version), Type> byIdentity = [];
    private readonly Dictionary<(string Name, int Version), DecodePath> upcastPaths = [];
    private readonly JsonSerializerOptions options;

    public JsonEventCodec(
        JsonSerializerOptions options,
        IEnumerable<EventRegistration<TEvent>> registrations,
        IEnumerable<JsonEventUpcaster>? upcasters = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (EventRegistration<TEvent> registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (!byType.TryAdd(registration.EventType, registration))
                throw new ArgumentException(
                    "An event CLR type has more than one write registration.",
                    nameof(registrations)
                );
            if (
                !byIdentity.TryAdd(
                    (registration.EventName, registration.SchemaVersion),
                    registration.EventType
                )
            )
                throw new ArgumentException(
                    "A durable event name/version has more than one registration.",
                    nameof(registrations)
                );
        }
        ConfigureUpcasting(upcasters ?? []);
        this.options = new JsonSerializerOptions(options);
        this.options.MakeReadOnly(populateMissingResolver: true);
    }

    public SerializedEvent Serialize(TEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        Type type = @event.GetType();
        if (!byType.TryGetValue(type, out var registration))
            throw new InvalidOperationException("The event runtime type is not registered.");
        return new SerializedEvent(
            registration.EventName,
            registration.SchemaVersion,
            JsonSerializer.SerializeToElement(@event, type, options)
        );
    }

    public TEvent Deserialize(string eventName, int schemaVersion, JsonElement payload)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        var identity = (eventName, schemaVersion);
        upcastPaths.TryGetValue(identity, out DecodePath? path);
        if (!byIdentity.TryGetValue(identity, out Type? type) && path is null)
            throw new EventDecodingException(
                eventName,
                schemaVersion,
                EventDecodingFailure.UnknownEvent
            );
        if (payload.ValueKind == JsonValueKind.Undefined)
            throw new EventDecodingException(
                eventName,
                schemaVersion,
                EventDecodingFailure.InvalidPayload
            );
        try
        {
            if (path is not null)
            {
                // Own every intermediate document lifetime without changing the caller's element.
                ValidateUpcastPayload(payload, eventName, schemaVersion);
                payload = payload.Clone();
                foreach (JsonEventUpcaster step in path.Steps)
                {
                    payload = step.Upcast(payload);
                    ValidateUpcastPayload(payload, eventName, schemaVersion);
                    payload = payload.Clone();
                }
                type = path.EventType;
            }

            return (TEvent?)payload.Deserialize(type!, options)
                ?? throw new EventDecodingException(
                    eventName,
                    schemaVersion,
                    EventDecodingFailure.InvalidPayload
                );
        }
        catch (JsonException exception)
        {
            throw new EventDecodingException(
                eventName,
                schemaVersion,
                EventDecodingFailure.InvalidPayload,
                exception
            );
        }
    }

    private void ConfigureUpcasting(IEnumerable<JsonEventUpcaster> upcasters)
    {
        Dictionary<(string Name, int Version), JsonEventUpcaster> steps = [];
        foreach (JsonEventUpcaster step in upcasters)
        {
            ArgumentNullException.ThrowIfNull(step);
            var source = (step.EventName, step.FromSchemaVersion);
            if (byIdentity.ContainsKey(source) || !steps.TryAdd(source, step))
                throw new ArgumentException(
                    "An upcaster source has more than one interpretation.",
                    nameof(upcasters)
                );
        }

        // A unique, strictly increasing path must end at an explicit CLR registration.
        // Resolve once during construction; decoding never guesses a nearest schema.
        foreach (var source in steps.Keys)
        {
            List<JsonEventUpcaster> path = [];
            var current = source;
            while (!byIdentity.ContainsKey(current))
            {
                if (!steps.TryGetValue(current, out JsonEventUpcaster? step))
                    throw new ArgumentException(
                        "An upcaster path does not reach a registered event schema.",
                        nameof(upcasters)
                    );
                path.Add(step);
                current = (step.EventName, step.ToSchemaVersion);
            }
            upcastPaths.Add(source, new DecodePath(byIdentity[current], path.ToArray()));
        }
    }

    private static void ValidateUpcastPayload(
        JsonElement payload,
        string eventName,
        int schemaVersion
    )
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new EventDecodingException(
                eventName,
                schemaVersion,
                EventDecodingFailure.InvalidPayload
            );
    }

    private sealed record DecodePath(Type EventType, JsonEventUpcaster[] Steps);
}
