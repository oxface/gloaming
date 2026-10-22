namespace Gloaming.EventSourcing;

/// <summary>A stream observation and its ordered, newly accepted facts, without persistence fields.</summary>
public interface IEventSourcedAggregate<out TEvent>
    where TEvent : class
{
    /// <summary>The consumer aggregate and stream identity.</summary>
    Guid Id { get; }

    /// <summary>The observed committed version captured when this aggregate was loaded; accepted decisions do not advance it.</summary>
    long ExpectedVersion { get; }

    /// <summary>The proposed stream version including all accepted pending events.</summary>
    long Version { get; }

    /// <summary>The accepted ordered event batch proposed since ExpectedVersion, without clearing it or saving.</summary>
    IReadOnlyList<TEvent> PendingEvents { get; }
}
