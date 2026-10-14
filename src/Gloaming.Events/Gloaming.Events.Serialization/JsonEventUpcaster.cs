using System.Text.Json;

namespace Gloaming.Events.Serialization;

/// <summary>A consumer-owned forward schema transformation under one durable event name.</summary>
public abstract class JsonEventUpcaster
{
    protected JsonEventUpcaster(string eventName, int fromSchemaVersion, int toSchemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentOutOfRangeException.ThrowIfLessThan(fromSchemaVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toSchemaVersion, fromSchemaVersion);
        EventName = eventName;
        FromSchemaVersion = fromSchemaVersion;
        ToSchemaVersion = toSchemaVersion;
    }

    public string EventName { get; }
    public int FromSchemaVersion { get; }
    public int ToSchemaVersion { get; }

    /// <summary>Explicitly permits compatible JSON to use a later schema's native defaults.</summary>
    public static JsonEventUpcaster PassThrough(
        string eventName,
        int fromSchemaVersion,
        int toSchemaVersion
    ) => new IdentityUpcaster(eventName, fromSchemaVersion, toSchemaVersion);

    /// <summary>Returns a live, defined, nonnull element. Implementations must be pure and thread-safe.</summary>
    public abstract JsonElement Upcast(JsonElement payload);

    private sealed class IdentityUpcaster(string eventName, int fromVersion, int toVersion)
        : JsonEventUpcaster(eventName, fromVersion, toVersion)
    {
        public override JsonElement Upcast(JsonElement payload) => payload;
    }
}
