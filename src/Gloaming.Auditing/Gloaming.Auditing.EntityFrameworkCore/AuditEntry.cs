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

    /// <summary>Consumer-assigned audit entry identity.</summary>
    public Guid Id { get; }

    /// <summary>Consumer-supplied UTC time of the selected audited action, not a database commit timestamp.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>The executing actor and optional trusted initiator captured for the audited action.</summary>
    public ActorContext Attribution { get; }

    /// <summary>Consumer-owned source vocabulary for the audited action.</summary>
    public string Source { get; }

    /// <summary>Consumer-owned action name; staging does not infer which actions must be audited.</summary>
    public string Action { get; }

    /// <summary>Consumer-owned type of the business subject.</summary>
    public string SubjectType { get; }

    /// <summary>Opaque item/aggregate identity; null denotes a subject-wide or global event.</summary>
    public string? SubjectKey { get; }

    /// <summary>The consumer-selected audited outcome; it does not independently establish commit.</summary>
    public string Outcome { get; }

    /// <summary>Positive version of the consumer safe-details schema.</summary>
    public int SchemaVersion { get; }

    /// <summary>The retained JSON value of consumer-classified safe details.</summary>
    public JsonElement Details { get; }

    /// <summary>Optional consumer-selected business explanation, distinct from the outcome.</summary>
    public string? ReasonCode { get; }

    /// <summary>Optional opaque tenant attribution; it neither admits the tenant nor authorizes reading this audit.</summary>
    public string? TenantKey { get; }
}
