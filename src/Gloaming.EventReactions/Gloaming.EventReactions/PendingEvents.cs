namespace Gloaming.EventReactions;

/// <summary>A consumer-composed ordered buffer for one serialized operation; it is not durable storage.</summary>
public sealed class PendingEvents : IEventSink
{
    private static long _nextSequence;
    private readonly List<Occurrence> _pending = [];

    /// <summary>The number of captured occurrences not yet drained by dispatch.</summary>
    public int Count => _pending.Count;

    /// <summary>Captures one nonnull occurrence without executing it or deduplicating equal payloads.</summary>
    public void Raise(object fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        // One process-local capture order spans every buffer without injecting operation
        // infrastructure into aggregates. The sequence is neither persisted nor exposed.
        long sequence = Interlocked.Increment(ref _nextSequence);
        if (sequence <= 0)
            throw new InvalidOperationException("The domain-event capture sequence is exhausted.");

        _pending.Add(new(sequence, fact));
    }

    internal Occurrence[] TakeAll()
    {
        var events = _pending.ToArray();
        _pending.Clear();
        return events;
    }

    internal readonly record struct Occurrence(long Sequence, object Fact);
}
