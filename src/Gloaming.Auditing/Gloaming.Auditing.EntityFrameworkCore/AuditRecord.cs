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

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public ActorKind ActorKind { get; private set; }

    public string? ActorKey { get; private set; }

    public ActorKind? InitiatorKind { get; private set; }

    public string? InitiatorKey { get; private set; }

    public string Source { get; private set; } = null!;

    public string Action { get; private set; } = null!;

    public string SubjectType { get; private set; } = null!;

    public string? SubjectKey { get; private set; }

    public string Outcome { get; private set; } = null!;

    public int SchemaVersion { get; private set; }

    public JsonElement Details { get; private set; }

    public string? ReasonCode { get; private set; }

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
