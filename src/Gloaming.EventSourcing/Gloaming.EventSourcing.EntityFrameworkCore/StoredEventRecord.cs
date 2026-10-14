using System.Text.Json;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>
/// Optional default event envelope for ordinary StreamId identities. Each payload retains its
/// registered event shape; provider JSON mapping, durable encoding and saving are consumer-owned.
/// </summary>
public sealed class StoredEventRecord : IStoredEventRecord
{
    public Guid EventId { get; set; }
    public Guid StreamId { get; set; }
    public long StreamVersion { get; set; }
    public string EventName { get; set; } = null!;
    public int SchemaVersion { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public JsonElement Payload { get; set; }
}
