namespace Gloaming.EventSourcing.EntityFrameworkCore;

public sealed record EventSourcingStorageOptions
{
    public string StreamsTable { get; init; } = "event_streams";
    public string EventsTable { get; init; } = "events";
    public string? Schema { get; init; }
}
