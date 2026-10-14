namespace Gloaming.EventSourcing;

/// <summary>A stream observation and its ordered, newly accepted facts, without persistence fields.</summary>
public interface IEventSourcedAggregate<out TEvent>
    where TEvent : class
{
    Guid Id { get; }
    long ExpectedVersion { get; }
    long Version { get; }
    IReadOnlyList<TEvent> PendingEvents { get; }
}
