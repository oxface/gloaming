namespace Gloaming.EventReactions;

public interface IEventReaction<in TEvent>
    where TEvent : notnull
{
    Task HandleAsync(TEvent fact, CancellationToken cancellationToken);
}

public interface ISynchronousEventReaction<in TEvent>
    where TEvent : notnull
{
    void Handle(TEvent fact);
}

public interface IEventSink
{
    void Raise(object fact);
}

public interface IEventSink<TModule> : IEventSink { }
