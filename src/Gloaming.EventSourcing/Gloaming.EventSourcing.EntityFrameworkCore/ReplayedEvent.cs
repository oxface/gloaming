namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>One decoded historical event retaining its validated stream position and append time.</summary>
/// <param name="StreamVersion">The contiguous position within the captured stream.</param>
/// <param name="RecordedAt">UTC application append time, independent of commit time.</param>
/// <param name="Event">The consumer-decoded fact used by deterministic historical evolution.</param>
public sealed record ReplayedEvent<TEvent>(
    long StreamVersion,
    DateTimeOffset RecordedAt,
    TEvent Event
)
    where TEvent : class;
