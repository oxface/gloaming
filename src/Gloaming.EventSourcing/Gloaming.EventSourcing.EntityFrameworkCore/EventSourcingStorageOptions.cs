namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Consumer-selected table/schema names for native stream and stored-event records.</summary>
public sealed record EventSourcingStorageOptions
{
    /// <summary>The consumer-owned table for stream identity and committed version metadata.</summary>
    public string StreamsTable { get; init; } = "event_streams";

    /// <summary>The append-only stored-event envelope table.</summary>
    public string EventsTable { get; init; } = "events";

    /// <summary>The consumer-owned native database schema for these mappings.</summary>
    public string? Schema { get; init; }
}
