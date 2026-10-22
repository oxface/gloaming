namespace Gloaming.EventReactions;

/// <summary>One consumer-selected asynchronous reaction to an exact event type.</summary>
public interface IEventReaction<in TEvent>
    where TEvent : notnull
{
    /// <summary>Handles one captured occurrence inside the caller-owned operation and transaction; external effects need durable handoff.</summary>
    Task HandleAsync(TEvent fact, CancellationToken cancellationToken);
}

/// <summary>A reaction that also supports native synchronous saving without blocking on asynchronous work.</summary>
public interface ISynchronousEventReaction<in TEvent>
    where TEvent : notnull
{
    /// <summary>Handles one occurrence synchronously within the owning native operation.</summary>
    void Handle(TEvent fact);
}

/// <summary>Captures ordinary event objects for later explicit reaction dispatch.</summary>
public interface IEventSink
{
    /// <summary>Records one nonnull occurrence in capture order without executing reactions.</summary>
    void Raise(object fact);
}

/// <summary>A scoped event sink selected for one consumer module and serialized operation.</summary>
public interface IEventSink<TModule> : IEventSink { }
