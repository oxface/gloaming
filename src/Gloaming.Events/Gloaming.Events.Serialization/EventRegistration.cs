using System.Diagnostics.CodeAnalysis;

namespace Gloaming.Events.Serialization;

/// <summary>Explicitly associates one concrete event type with its durable write identity.</summary>
public sealed class EventRegistration<TEvent>
    where TEvent : class
{
    private EventRegistration(Type eventType, string eventName, int schemaVersion)
    {
        EventType = eventType;
        EventName = eventName;
        SchemaVersion = schemaVersion;
    }

    internal Type EventType { get; }

    /// <summary>Stable consumer-selected durable alias, independent of the CLR type name.</summary>
    public string EventName { get; }

    /// <summary>Positive schema version written by this concrete event registration.</summary>
    public int SchemaVersion { get; }

    /// <summary>Registers the exact concrete CLR event type with an explicit durable alias and positive schema version.</summary>
    [SuppressMessage(
        "Design",
        "CA1000:Do not declare static members on generic types",
        Justification = "The family type scopes registration; the factory selects its concrete event type."
    )]
    public static EventRegistration<TEvent> For<TConcrete>(string eventName, int schemaVersion)
        where TConcrete : class, TEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        Type type = typeof(TConcrete);
        if (!type.IsClass || type.IsAbstract)
            throw new ArgumentException("Register a concrete reference event type.");
        return new EventRegistration<TEvent>(type, eventName, schemaVersion);
    }
}
