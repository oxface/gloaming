using System.Text.Json;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Consumer-populated durable envelope fields; encoding and event meaning stay outside mapping.</summary>
public interface IStoredEventRecord
{
    Guid EventId { get; set; }
    Guid StreamId { get; set; }
    long StreamVersion { get; set; }
    string EventName { get; set; }
    int SchemaVersion { get; set; }
    DateTimeOffset RecordedAt { get; set; }
    JsonElement Payload { get; set; }
}
