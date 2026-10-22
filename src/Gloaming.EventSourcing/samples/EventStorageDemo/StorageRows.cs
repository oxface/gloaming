using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.EventStorageDemo;

public sealed class EventStreamRecord : IEventStreamRecord
{
    public Guid Id { get; set; }
    public string StreamType { get; set; } = null!;
    public long Version { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? Description { get; set; }
}

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
