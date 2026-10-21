using System.Text.Json;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Retained delivery and local processing lifecycle; native operations own mutations.</summary>
public sealed class InboxMessageRecord
{
    private InboxMessageRecord() { }

    /// <summary>Stable receiver processing registration, part of the delivery key.</summary>
    public string SubscriptionKey { get; private set; } = null!;

    /// <summary>Receiver-assigned producer namespace, part of the delivery key.</summary>
    public string ProducerKey { get; private set; } = null!;

    /// <summary>Stable delivery identity, part of the delivery key.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>Wire payload contract alias.</summary>
    public string MessageName { get; private set; } = null!;

    /// <summary>Positive wire payload version.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>Retained JSON value, not its original lexical representation.</summary>
    public JsonElement Payload { get; private set; }

    /// <summary>Optional tenant metadata; admission belongs to consumer code.</summary>
    public string? TenantKey { get; private set; }

    /// <summary>Optional conversation identity, not a deduplication identity.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Optional immediate cause identity.</summary>
    public string? CausationId { get; private set; }

    /// <summary>Retained diagnostic upstream context, independent of delivery deduplication.</summary>
    public string? TraceParent { get; private set; }

    /// <summary>Retained opaque vendor state accompanying TraceParent.</summary>
    public string? TraceState { get; private set; }

    /// <summary>Database insertion time, independent of business occurrence time.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Earliest database time eligible for processing.</summary>
    public DateTimeOffset AvailableAt { get; private set; }

    /// <summary>Database time of committed local handling, or null while pending.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Monotonic revision of committed failure, completion and operator retry transitions.</summary>
    public long Revision { get; private set; }

    /// <summary>Lifetime committed observations of failed local work; excludes cancellation and unrecorded crashes.</summary>
    public long FailureCount { get; private set; }

    /// <summary>Confirmed failures since the latest applied operator retry.</summary>
    public long FailuresSinceRedrive { get; private set; }

    /// <summary>Database time of the latest recorded failure.</summary>
    public DateTimeOffset? LastFailedAt { get; private set; }

    /// <summary>Safe consumer reason; exception text and payloads do not belong here.</summary>
    public string? LastFailureCode { get; private set; }

    /// <summary>Database time automatic processing stopped, or null when not quarantined.</summary>
    public DateTimeOffset? QuarantinedAt { get; private set; }

    /// <summary>Number of committed operator retry transitions.</summary>
    public long RedriveCount { get; private set; }

    /// <summary>Database time of the latest applied operator retry.</summary>
    public DateTimeOffset? LastRedrivenAt { get; private set; }
}
