using System.Text.Json;

namespace Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;

/// <summary>Read-only persisted generation evidence. Consumer migrations and retention own its lifetime.</summary>
public sealed class RecurringIntentRecord
{
    private RecurringIntentRecord() { }

    /// <summary>Complete ordinal technical scope.</summary>
    public string ScopeKey { get; private set; } = null!;

    /// <summary>Consumer schedule identity.</summary>
    public Guid ScheduleId { get; private set; }

    /// <summary>Immutable generation identity.</summary>
    public Guid GenerationId { get; private set; }

    /// <summary>Versioned immutable evaluator/calendar descriptor; retained as exact text.</summary>
    public string Definition { get; private set; } = null!;

    /// <summary>Opaque versioned planner progress; applications must not edit it.</summary>
    public string Progress { get; private set; } = null!;

    /// <summary>Monotonic native concurrency token.</summary>
    public long Revision { get; private set; }

    /// <summary>Retained lifecycle state.</summary>
    public RecurringIntentState State { get; private set; }

    internal static RecurringIntentRecord Create(
        RecurringIntentKey key,
        RecurrenceDefinition definition
    ) =>
        new()
        {
            ScopeKey = key.ScopeKey,
            ScheduleId = key.ScheduleId,
            GenerationId = key.GenerationId,
            Definition = definition.Descriptor,
            Progress = JsonSerializer.Serialize(
                new RecurrenceCursor(1, definition.Next(definition.Anchor), null, null, null, null)
            ),
            Revision = 1,
            State = RecurringIntentState.Active,
        };

    internal RecurrenceCursor Cursor()
    {
        var cursor = JsonSerializer.Deserialize<RecurrenceCursor>(Progress);
        if (cursor is null || cursor.Protocol != 1 || Revision < 1 || !Enum.IsDefined(State))
            throw new InvalidDataException("Unsupported or invalid retained recurrence progress.");

        foreach (
            var instant in new[]
            {
                cursor.Next,
                cursor.Armed,
                cursor.Observed,
                cursor.Window,
                cursor.Candidate,
            }
        )
            if (instant is { } value)
                RecurrenceDefinition.RequireInstant(value);

        if (
            cursor.Window is null && cursor.Candidate is not null
            || cursor.Window is { } cutoff
                && (
                    cursor.Observed is null || cutoff > cursor.Observed || cursor.Candidate > cutoff
                )
            || cursor.Armed is { } armed
                && (cursor.Next is { } next && next <= armed || cursor.Window is not null)
        )
            throw new InvalidDataException("Inconsistent retained recurrence progress.");

        return cursor;
    }

    internal void Advance(RecurrenceCursor cursor, RecurringIntentState state)
    {
        Progress = JsonSerializer.Serialize(cursor);
        State = state;
        Revision = checked(Revision + 1);
    }

    internal void Stop(RecurringIntentState state)
    {
        State = state;
        Revision = checked(Revision + 1);
    }
}

internal sealed record RecurrenceCursor(
    int Protocol,
    DateTimeOffset? Next,
    DateTimeOffset? Armed,
    DateTimeOffset? Observed,
    DateTimeOffset? Window,
    DateTimeOffset? Candidate
);
