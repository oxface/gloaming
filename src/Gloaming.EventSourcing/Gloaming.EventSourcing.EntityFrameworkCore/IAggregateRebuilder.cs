namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Reconstructs one inline aggregate from retained history. The caller saves and commits.</summary>
public interface IAggregateRebuilder<TAggregate>
    where TAggregate : class
{
    /// <summary>Replays one captured stream and proposes its inline-state repair in the existing transaction; the caller owns save and commit.</summary>
    Task<AggregateRebuildResult?> RebuildAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Captured event-head metadata of reconstructed state. Commit remains caller-owned.</summary>
public sealed record AggregateRebuildResult(long Version, DateTimeOffset RecordedAt);
