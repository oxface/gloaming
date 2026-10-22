using System.Text.Json;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>
/// Optional default event envelope for ordinary StreamId identities. Each payload retains its
/// registered event shape; provider JSON mapping, durable encoding and saving are consumer-owned.
/// </summary>
public sealed class StoredEventRecord : IStoredEventRecord
{
    /// <summary>Unique identity of the retained event envelope.</summary>
    public Guid EventId { get; set; }

    /// <summary>The owning stream identity.</summary>
    public Guid StreamId { get; set; }

    /// <summary>The contiguous position of this event within its stream.</summary>
    public long StreamVersion { get; set; }

    /// <summary>Stable consumer-selected durable event alias.</summary>
    public string EventName { get; set; } = null!;

    /// <summary>Positive durable payload schema version.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>UTC application append time, distinct from commit time.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>The retained JSON event payload; decoding and evolution remain consumer-owned.</summary>
    public JsonElement Payload { get; set; }
}
