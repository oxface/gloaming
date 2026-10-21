using System.Text.Json;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Provided durable envelope and delivery lifecycle. Mutations belong to enqueue and dispatch.</summary>
public sealed class OutboxMessageRecord
{
    private OutboxMessageRecord() { }

    internal static OutboxMessageRecord Create(OutgoingMessage message) =>
        new()
        {
            MessageId = message.MessageId,
            RouteKey = message.RouteKey,
            MessageName = message.MessageName,
            SchemaVersion = message.SchemaVersion,
            Payload = message.Payload,
            TenantKey = message.TenantKey,
            CorrelationId = message.CorrelationId,
            CausationId = message.CausationId,
            TraceParent = message.TraceParent,
            TraceState = message.TraceState,
        };

    /// <summary>Retained delivery identity and primary key within this module's outbox.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>Consumer-defined logical routing key interpreted by its publisher.</summary>
    public string RouteKey { get; private set; } = null!;

    /// <summary>Stable wire-contract alias, independent of the CLR type and routing key.</summary>
    public string MessageName { get; private set; } = null!;

    /// <summary>Positive version of the named wire contract.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>Retained wire payload; PostgreSQL JSONB preserves values rather than lexical formatting.</summary>
    public JsonElement Payload { get; private set; }

    /// <summary>Optional opaque tenant metadata; does not establish trusted tenant admission.</summary>
    public string? TenantKey { get; private set; }

    /// <summary>Optional retained conversation identity; not a deduplication key.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Optional retained identity of the immediate cause.</summary>
    public string? CausationId { get; private set; }

    /// <summary>Retained diagnostic upstream context, independent of delivery deduplication.</summary>
    public string? TraceParent { get; private set; }

    /// <summary>Retained opaque vendor state accompanying TraceParent.</summary>
    public string? TraceState { get; private set; }

    /// <summary>Database-assigned insertion time, not the business event's occurrence time.</summary>
    public DateTimeOffset QueuedAt { get; private set; }

    /// <summary>Earliest database time at which an undispatched row may be claimed.</summary>
    public DateTimeOffset AvailableAt { get; private set; }

    /// <summary>Database time of recorded transport acceptance; null until dispatch completes.</summary>
    public DateTimeOffset? DispatchedAt { get; private set; }

    /// <summary>Identity of the current claim, used to fence completion and retry writes.</summary>
    public Guid? LeaseToken { get; private set; }

    /// <summary>Database-clock claim expiry; null when no claim is held.</summary>
    public DateTimeOffset? LeaseUntil { get; private set; }

    /// <summary>Number of committed claims, including claims abandoned before publication.</summary>
    public long Attempts { get; private set; }

    /// <summary>Monotonic revision of committed lifecycle transitions.</summary>
    public long Revision { get; private set; }

    /// <summary>Lifetime committed observations of publisher exceptions; not confirmed transport rejection.</summary>
    public long FailureCount { get; private set; }

    /// <summary>Recorded publisher exceptions since the latest applied operator retry.</summary>
    public long FailuresSinceRedrive { get; private set; }

    /// <summary>Database time of the latest recorded publisher exception.</summary>
    public DateTimeOffset? LastFailedAt { get; private set; }

    /// <summary>Consumer-safe reason for the latest observation, excluding exception text.</summary>
    public string? LastFailureCode { get; private set; }

    /// <summary>Database time automatic claims stopped; cannot cancel an obsolete in-flight publisher.</summary>
    public DateTimeOffset? DeadLetteredAt { get; private set; }

    /// <summary>Number of applied operator retries.</summary>
    public long RedriveCount { get; private set; }

    /// <summary>Database time of the latest applied operator retry.</summary>
    public DateTimeOffset? LastRedrivenAt { get; private set; }
}
