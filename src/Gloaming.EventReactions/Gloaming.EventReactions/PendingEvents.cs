namespace Gloaming.EventReactions;

public sealed class PendingEvents : IEventSink
{
    private static long _nextSequence;
    private readonly List<Occurrence> _pending = [];

    public int Count => _pending.Count;

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
