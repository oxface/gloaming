using System.Security.Cryptography;
using System.Text;

namespace Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;

/// <summary>The complete consumer scope, schedule identity and retained immutable generation.</summary>
public sealed record RecurringIntentKey
{
    /// <summary>An ordinal technical scope; it is not inferred from envelope tenancy.</summary>
    public string ScopeKey { get; }

    /// <summary>The consumer's schedule identity.</summary>
    public Guid ScheduleId { get; }

    /// <summary>A nonreusable generation identity selected by the consumer.</summary>
    public Guid GenerationId { get; }

    /// <summary>Validates a complete identity without I/O.</summary>
    public RecurringIntentKey(string scopeKey, Guid scheduleId, Guid generationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scopeKey.Length, 256);
        if (scheduleId == Guid.Empty || generationId == Guid.Empty)
            throw new ArgumentException("Use nonempty schedule and generation identities.");

        ScopeKey = scopeKey;
        ScheduleId = scheduleId;
        GenerationId = generationId;
    }

    internal Guid Identity(string purpose, DateTimeOffset? nominal = null)
    {
        // Length framing avoids scope/delimiter ambiguity. Hex GUIDs and invariant ticks
        // are retained protocol v1; never replace this with a process hash or random ID.
        string text = FormattableString.Invariant(
            $"gloaming.recurrence.v1|{Encoding.UTF8.GetByteCount(ScopeKey)}:{ScopeKey}|{ScheduleId:N}|{GenerationId:N}|{purpose}|{nominal?.Ticks}"
        );
        return new Guid(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16),
            bigEndian: true
        );
    }
}

/// <summary>Persisted lifecycle values. Released finite deliveries remain receiver-owned after stopping.</summary>
public enum RecurringIntentState
{
    /// <summary>Planning is permitted.</summary>
    Active = 1,

    /// <summary>Planning is disabled; bounded cancellation may remain.</summary>
    Stopping = 2,

    /// <summary>No pending finite occurrences remained at the final locked cancellation observation.</summary>
    Stopped = 3,
}

/// <summary>Identity and nominal UTC instant supplied to a pure consumer envelope factory.</summary>
public sealed record RecurringOccurrence(Guid MessageId, Guid OccurrenceId, DateTimeOffset DueAt);

/// <summary>A bounded planning proposal; counts and progress become durable only after native save and commit.</summary>
public sealed record RecurrencePlan(int Staged, int Examined, bool MoreWork);

/// <summary>A bounded suppression proposal; released occurrences cannot be recalled.</summary>
public sealed record RecurrenceStop(int Suppressed, bool MoreWork);
