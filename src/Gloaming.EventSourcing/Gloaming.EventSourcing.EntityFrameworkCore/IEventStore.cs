namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>A configured aggregate write store. The caller owns the native transaction and save.</summary>
public interface IEventStore<TAggregate>
    where TAggregate : class
{
    /// <summary>Loads an aggregate and its consistent observed stream version in the owning write transaction; optional expected version rejects stale callers.</summary>
    Task<TAggregate?> GetForWritingAsync(
        Guid id,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Proposes the aggregate accepted pending batch, stream metadata and required inline state against its observed version; does not save or commit.</summary>
    Task<EventAppendResult> AppendAsync(
        TAggregate aggregate,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Version and recorded time of a batch added to the native unit of work. Commit remains caller-owned.</summary>
public sealed record EventAppendResult(long Version, DateTimeOffset RecordedAt);
