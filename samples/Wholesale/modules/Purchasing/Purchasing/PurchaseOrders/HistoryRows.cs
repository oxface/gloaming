using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal sealed class EventStream : IEventStreamRecord
{
    public string OrganizationKey { get; set; } = null!;
    public Guid Id { get; set; }
    public string StreamType { get; set; } = null!;
    public long Version { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class StoredEvent : IStoredEventRecord
{
    public string OrganizationKey { get; set; } = null!;
    public Guid EventId { get; set; }
    public Guid StreamId { get; set; }
    public long StreamVersion { get; set; }
    public string EventName { get; set; } = null!;
    public int SchemaVersion { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public JsonElement Payload { get; set; }
}
