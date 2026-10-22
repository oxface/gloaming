using System.Text.Json;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Consumer-populated durable envelope fields; encoding and event meaning stay outside mapping.</summary>
public interface IStoredEventRecord
{
    /// <summary>Unique identity of the retained event envelope.</summary>
    Guid EventId { get; set; }

    /// <summary>The owning stream identity.</summary>
    Guid StreamId { get; set; }

    /// <summary>The contiguous position of this event within its stream.</summary>
    long StreamVersion { get; set; }

    /// <summary>Stable consumer-selected durable event alias.</summary>
    string EventName { get; set; }

    /// <summary>Positive durable payload schema version.</summary>
    int SchemaVersion { get; set; }

    /// <summary>UTC application append time, distinct from commit time.</summary>
    DateTimeOffset RecordedAt { get; set; }

    /// <summary>The retained JSON event payload; decoding and evolution remain consumer-owned.</summary>
    JsonElement Payload { get; set; }
}
