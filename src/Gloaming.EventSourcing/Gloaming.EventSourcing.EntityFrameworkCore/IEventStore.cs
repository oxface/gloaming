namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>A configured aggregate write store. The caller owns the native transaction and save.</summary>
public interface IEventStore<TAggregate>
    where TAggregate : class
{
    Task<TAggregate?> GetForWritingAsync(
        Guid id,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default
    );

    Task<EventAppendResult> AppendAsync(
        TAggregate aggregate,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Version and recorded time of a batch added to the native unit of work. Commit remains caller-owned.</summary>
public sealed record EventAppendResult(long Version, DateTimeOffset RecordedAt);
