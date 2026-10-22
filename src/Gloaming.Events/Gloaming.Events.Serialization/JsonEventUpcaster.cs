using System.Text.Json;

namespace Gloaming.Events.Serialization;

/// <summary>A consumer-owned forward schema transformation under one durable event name.</summary>
public abstract class JsonEventUpcaster
{
    /// <summary>Declares one forward schema edge for a stable event alias; no payload transformation runs during construction.</summary>
    protected JsonEventUpcaster(string eventName, int fromSchemaVersion, int toSchemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentOutOfRangeException.ThrowIfLessThan(fromSchemaVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toSchemaVersion, fromSchemaVersion);
        EventName = eventName;
        FromSchemaVersion = fromSchemaVersion;
        ToSchemaVersion = toSchemaVersion;
    }

    /// <summary>The durable event alias to which this forward transformation applies.</summary>
    public string EventName { get; }

    /// <summary>The positive source payload schema version.</summary>
    public int FromSchemaVersion { get; }

    /// <summary>The strictly later payload schema version produced by this transformation.</summary>
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
