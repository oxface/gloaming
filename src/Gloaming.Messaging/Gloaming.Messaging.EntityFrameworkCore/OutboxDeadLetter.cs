namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Detached current dead-letter metadata; excludes payload, tenant, diagnostic context, lease token and exception text.</summary>
/// <param name="MessageId">Retained identity in the owning outbox.</param>
/// <param name="RouteKey">Consumer-selected logical route.</param>
/// <param name="MessageName">Wire contract alias.</param>
/// <param name="SchemaVersion">Wire contract version.</param>
/// <param name="Revision">Observed lifecycle revision, rechecked by retry.</param>
/// <param name="Attempts">Committed claims, including abandoned claims; not confirmed failures.</param>
/// <param name="FailureCount">Lifetime committed publisher-exception observations.</param>
/// <param name="FailuresSinceRedrive">Such observations since the latest applied retry.</param>
/// <param name="QueuedAt">Original database insertion time.</param>
/// <param name="DeadLetteredAt">Database time automatic claims stopped.</param>
/// <param name="LastFailureCode">Latest consumer-safe reason.</param>
/// <param name="LastFailedAt">Database time of the latest recorded exception observation.</param>
/// <param name="RedriveCount">Applied operator retries.</param>
/// <param name="LastRedrivenAt">Database time of the latest applied retry, if any.</param>
public sealed record OutboxDeadLetter(
    Guid MessageId,
    string RouteKey,
    string MessageName,
    int SchemaVersion,
    long Revision,
    long Attempts,
    long FailureCount,
    long FailuresSinceRedrive,
    DateTimeOffset QueuedAt,
    DateTimeOffset DeadLetteredAt,
    string LastFailureCode,
    DateTimeOffset LastFailedAt,
    long RedriveCount,
    DateTimeOffset? LastRedrivenAt
);
