using System.Text.Json;

namespace Gloaming.Events.Serialization;

/// <summary>Encoded event identity and payload, without stream or transport metadata.</summary>
public sealed record SerializedEvent(string EventName, int SchemaVersion, JsonElement Payload);
