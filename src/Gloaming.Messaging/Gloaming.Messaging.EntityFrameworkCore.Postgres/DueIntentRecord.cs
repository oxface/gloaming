using System.Text.Json;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Provided immutable occurrence envelope. Lifecycle mutations belong to PostgresDueIntents.</summary>
public sealed class DueIntentRecord
{
    private DueIntentRecord() { }

    internal static DueIntentRecord Create(
        DueIntentKey key,
        DateTimeOffset dueAt,
        OutgoingMessage message
    ) =>
        new()
        {
            ScopeKey = key.ScopeKey,
            ScheduleId = key.ScheduleId,
            OccurrenceId = key.OccurrenceId,
            DueAt = dueAt,
            State = DueIntentState.Pending,
            MessageId = message.MessageId,
            RouteKey = message.RouteKey,
            MessageName = message.MessageName,
            SchemaVersion = message.SchemaVersion,
            Payload = message.Payload.Clone(),
            TenantKey = message.TenantKey,
            CorrelationId = message.CorrelationId,
            CausationId = message.CausationId,
            TraceParent = message.TraceParent,
            TraceState = message.TraceState,
        };

    /// <summary>Opaque ordinal selection scope, not admission or tenant metadata.</summary>
    public string ScopeKey { get; private set; } = null!;

    /// <summary>Consumer-selected source schedule.</summary>
    public Guid ScheduleId { get; private set; }

    /// <summary>Immutable occurrence identity.</summary>
    public Guid OccurrenceId { get; private set; }

    /// <summary>Explicit UTC due instant at PostgreSQL microsecond precision.</summary>
    public DateTimeOffset DueAt { get; private set; }

    /// <summary>Retained pending, cancelled or released state.</summary>
    public DueIntentState State { get; private set; }

    /// <summary>Original outgoing delivery ID, independent of occurrence identity.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>Opaque outgoing route.</summary>
    public string RouteKey { get; private set; } = null!;

    /// <summary>Stable consumer wire-contract name.</summary>
    public string MessageName { get; private set; } = null!;

    /// <summary>Positive consumer wire schema.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>Owned opaque JSON; PostgreSQL JSONB retains values rather than lexical formatting.</summary>
    public JsonElement Payload { get; private set; }

    /// <summary>Optional outgoing tenant metadata; it grants no authority.</summary>
    public string? TenantKey { get; private set; }

    /// <summary>Optional original conversation.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Optional original cause.</summary>
    public string? CausationId { get; private set; }

    /// <summary>Optional original diagnostic upstream context.</summary>
    public string? TraceParent { get; private set; }

    /// <summary>Optional original diagnostic vendor state.</summary>
    public string? TraceState { get; private set; }

    internal void Transition(DueIntentState target) => State = target;

    internal OutgoingMessage Message() =>
        new(
            MessageId,
            RouteKey,
            MessageName,
            SchemaVersion,
            Payload,
            TenantKey,
            CorrelationId,
            CausationId,
            TraceParent,
            TraceState
        );
}
