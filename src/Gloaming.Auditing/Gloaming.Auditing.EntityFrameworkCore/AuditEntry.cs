using System.Text.Json;
using Gloaming.ActorIdentity;

namespace Gloaming.Auditing.EntityFrameworkCore;

/// <summary>An explicitly selected audit envelope; classification and trusted attribution belong to its consumer.</summary>
public sealed class AuditEntry
{
    /// <summary>Captures immutable attribution and owned JSON before staging. Observation time is not commit time.</summary>
    public AuditEntry(
        Guid id,
        DateTimeOffset occurredAt,
        ActorContext attribution,
        string source,
        string action,
        string subjectType,
        string? subjectKey,
        string outcome,
        int schemaVersion,
        JsonElement details,
        string? reasonCode = null,
        string? tenantKey = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        if (subjectKey is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);

        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (details.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new ArgumentException(
                "An audit requires non-null JSON details.",
                nameof(details)
            );
        if (reasonCode is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        if (tenantKey is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);

        Id = id;
        OccurredAt = occurredAt.ToUniversalTime();
        Attribution = attribution;
        Source = source;
        Action = action;
        SubjectType = subjectType;
        SubjectKey = subjectKey;
        Outcome = outcome;
        SchemaVersion = schemaVersion;
        Details = details.Clone();
        ReasonCode = reasonCode;
        TenantKey = tenantKey;
    }

    public Guid Id { get; }

    public DateTimeOffset OccurredAt { get; }

    public ActorContext Attribution { get; }

    public string Source { get; }

    public string Action { get; }

    public string SubjectType { get; }

    /// <summary>Opaque item/aggregate identity; null denotes a subject-wide or global event.</summary>
    public string? SubjectKey { get; }

    public string Outcome { get; }

    public int SchemaVersion { get; }

    public JsonElement Details { get; }

    /// <summary>Optional consumer-selected business explanation, distinct from the outcome.</summary>
    public string? ReasonCode { get; }

    public string? TenantKey { get; }
}
