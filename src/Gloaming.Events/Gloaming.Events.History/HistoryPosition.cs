namespace Gloaming.Events.History;

/// <summary>Stream position and recorded timestamp, independent of event payloads.</summary>
public readonly record struct HistoryPosition(long StreamVersion, DateTimeOffset RecordedAt);
