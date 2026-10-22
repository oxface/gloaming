using System.Text.Json;
using Gloaming.ActorIdentity;

namespace Gloaming.Auditing.EntityFrameworkCore;

/// <summary>Provided durable envelope. Consumer code owns queries, visibility and privileged retention.</summary>
public sealed class AuditRecord
{
    private AuditRecord() { }

    internal static AuditRecord Create(AuditEntry entry) =>
        new()
        {
            Id = entry.Id,
            OccurredAt = entry.OccurredAt,
            ActorKind = entry.Attribution.Actor.Kind,
            ActorKey = entry.Attribution.Actor.Id?.Value,
            InitiatorKind = entry.Attribution.Initiator?.Kind,
            InitiatorKey = entry.Attribution.Initiator?.Id?.Value,
            Source = entry.Source,
            Action = entry.Action,
            SubjectType = entry.SubjectType,
            SubjectKey = entry.SubjectKey,
            Outcome = entry.Outcome,
            SchemaVersion = entry.SchemaVersion,
            Details = entry.Details,
            ReasonCode = entry.ReasonCode,
            TenantKey = entry.TenantKey,
        };

    /// <summary>Consumer-assigned audit entry identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>Consumer-supplied UTC time of the selected audited action, not a database commit timestamp.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The persisted executing actor kind.</summary>
    public ActorKind ActorKind { get; private set; }

    /// <summary>Opaque executing actor key, absent for explicit anonymity.</summary>
    public string? ActorKey { get; private set; }

    /// <summary>Optional persisted kind of the trusted initiating actor.</summary>
    public ActorKind? InitiatorKind { get; private set; }

    /// <summary>Optional opaque trusted initiating actor key.</summary>
    public string? InitiatorKey { get; private set; }

    /// <summary>Consumer-owned source vocabulary for the audited action.</summary>
    public string Source { get; private set; } = null!;

    /// <summary>Consumer-owned action name; staging does not infer which actions must be audited.</summary>
    public string Action { get; private set; } = null!;

    /// <summary>Consumer-owned type of the business subject.</summary>
    public string SubjectType { get; private set; } = null!;

    /// <summary>Optional consumer-owned identity of the audited subject.</summary>
    public string? SubjectKey { get; private set; }

    /// <summary>The consumer-selected audited outcome; it does not independently establish commit.</summary>
    public string Outcome { get; private set; } = null!;

    /// <summary>Positive version of the consumer safe-details schema.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>The retained JSON value of consumer-classified safe details.</summary>
    public JsonElement Details { get; private set; }

    /// <summary>Optional safe consumer reason code, independent of exception text.</summary>
    public string? ReasonCode { get; private set; }

    /// <summary>Optional opaque tenant attribution; it neither admits the tenant nor authorizes reading this audit.</summary>
    public string? TenantKey { get; private set; }

    // EF entities retain reference identity. JSON must be compared explicitly: record
    // equality would compare JsonElement backing storage, not its contents. The pending
    // envelope guard preserves exact captured JSON text, rather than JSONB equivalence.
    internal bool Matches(AuditEntry entry) =>
        Id == entry.Id
        && OccurredAt == entry.OccurredAt
        && ActorKind == entry.Attribution.Actor.Kind
        && ActorKey == entry.Attribution.Actor.Id?.Value
        && InitiatorKind == entry.Attribution.Initiator?.Kind
        && InitiatorKey == entry.Attribution.Initiator?.Id?.Value
        && Source == entry.Source
        && Action == entry.Action
        && SubjectType == entry.SubjectType
        && SubjectKey == entry.SubjectKey
        && Outcome == entry.Outcome
        && SchemaVersion == entry.SchemaVersion
        && Details.ValueKind != JsonValueKind.Undefined
        && Details.GetRawText() == entry.Details.GetRawText()
        && ReasonCode == entry.ReasonCode
        && TenantKey == entry.TenantKey;
}
