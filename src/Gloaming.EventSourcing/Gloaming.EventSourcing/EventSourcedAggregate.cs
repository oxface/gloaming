namespace Gloaming.EventSourcing;

/// <summary>Atomic candidate and pending-fact bookkeeping for consumer-owned domain operations.</summary>
public abstract class EventSourcedAggregate<TState, TEvent> : IEventSourcedAggregate<TEvent>
    where TState : class
    where TEvent : class
{
    private readonly List<TEvent> pending = [];

    /// <summary>Initializes observed state without running current policy or collecting historical facts.</summary>
    protected EventSourcedAggregate(Guid id, long observedVersion, TState? observedState)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(observedVersion);
        if ((observedVersion == 0) != (observedState is null))
            throw new ArgumentException(
                "Supply state exactly for an existing aggregate.",
                nameof(observedState)
            );
        Id = id;
        ExpectedVersion = Version = observedVersion;
        State = observedState;
        PendingEvents = pending.AsReadOnly();
    }

    /// <summary>The consumer aggregate and stream identity.</summary>
    public Guid Id { get; }

    /// <summary>The observed committed version captured when this aggregate was loaded; accepted decisions do not advance it.</summary>
    public long ExpectedVersion { get; }

    /// <summary>The proposed stream version including all accepted pending events.</summary>
    public long Version { get; private set; }

    /// <summary>The accepted candidate state at Version; historical evolution and command eligibility remain consumer-owned.</summary>
    public TState? State { get; private set; }

    /// <summary>The accepted ordered event batch proposed since ExpectedVersion, without clearing it or saving.</summary>
    public IReadOnlyList<TEvent> PendingEvents { get; }

    /// <summary>Accepts the complete candidate only after pure evolution, policy and checked arithmetic succeed.</summary>
    protected void ApplyChanges(IReadOnlyList<TEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        TEvent[] batch = events.ToArray();
        foreach (TEvent @event in batch)
            ArgumentNullException.ThrowIfNull(@event);
        if (batch.Length == 0)
            return;
        long nextVersion = checked(Version + batch.Length);
        TState candidate =
            Evolve(State, batch)
            ?? throw new InvalidOperationException("Evolution must produce a candidate state.");
        ValidateCandidate(candidate);
        pending.AddRange(batch);
        State = candidate;
        Version = nextVersion;
    }

    /// <summary>Calculates the complete candidate using immutable state and facts, without side effects.</summary>
    protected abstract TState Evolve(TState? state, IReadOnlyList<TEvent> events);

    /// <summary>Validates a new decision's final candidate; never called while initializing historical state.</summary>
    protected abstract void ValidateCandidate(TState candidate);
}
