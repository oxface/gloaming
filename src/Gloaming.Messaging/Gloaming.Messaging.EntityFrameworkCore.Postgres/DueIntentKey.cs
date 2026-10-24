namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Consumer-selected scope, schedule and immutable occurrence identity; none grants authority.</summary>
public sealed record DueIntentKey
{
    /// <summary>Creates a complete occurrence key. A replacement uses another occurrence, not another delivery of this occurrence.</summary>
    public DueIntentKey(string scopeKey, Guid scheduleId, Guid occurrenceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scopeKey.Length, 256);
        ArgumentOutOfRangeException.ThrowIfEqual(scheduleId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(occurrenceId, Guid.Empty);

        ScopeKey = scopeKey;
        ScheduleId = scheduleId;
        OccurrenceId = occurrenceId;
    }

    /// <summary>Opaque ordinal selection key, independent of optional envelope tenancy.</summary>
    public string ScopeKey { get; }

    /// <summary>Consumer-selected source schedule identity.</summary>
    public Guid ScheduleId { get; }

    /// <summary>Identity of this finite occurrence, retained for the module lifetime.</summary>
    public Guid OccurrenceId { get; }
}

/// <summary>Retained lifecycle; terminal rows remain evidence and are not selected again.</summary>
public enum DueIntentState
{
    /// <summary>Eligible for suppression or release at its explicit due instant.</summary>
    Pending = 1,

    /// <summary>Suppressed before a committed handoff.</summary>
    Cancelled = 2,

    /// <summary>Atomically handed off to the owning outbox; cancellation cannot recall delivery.</summary>
    Released = 3,
}

/// <summary>Suppression proposal in the caller's transaction; final only after its commit.</summary>
public enum DueIntentSuppression
{
    /// <summary>The locked pending occurrence is staged as cancelled.</summary>
    Suppressed = 1,

    /// <summary>Retained cancellation already prevents release.</summary>
    AlreadyCancelled = 2,

    /// <summary>A committed handoff cannot be recalled by this operation.</summary>
    AlreadyReleased = 3,

    /// <summary>No visible occurrence matches the complete key.</summary>
    Missing = 4,
}
