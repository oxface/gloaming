namespace Gloaming.EventSourcing.EntityFrameworkCore;

public sealed record ReplayedEvent<TEvent>(
    long StreamVersion,
    DateTimeOffset RecordedAt,
    TEvent Event
)
    where TEvent : class;
