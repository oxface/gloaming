namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Consumer-owned state decoding and candidate encoding, without stream orchestration.</summary>
public abstract class AggregateStateMapping<TAggregate, TInlineStateRecord>
    where TAggregate : class
    where TInlineStateRecord : class, IInlineStateRecord
{
    public abstract TAggregate ToAggregate(TInlineStateRecord stateRow);

    /// <summary>Returns a fresh detached row containing candidate state; the store sets keys/version/time.</summary>
    public abstract TInlineStateRecord ToRow(TAggregate aggregate);
}
