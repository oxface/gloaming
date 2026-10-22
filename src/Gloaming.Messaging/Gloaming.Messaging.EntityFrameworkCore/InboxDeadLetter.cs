namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Detached metadata for one current dead letter; excludes payload, tenant, diagnostic context and exception text.</summary>
/// <param name="SubscriptionKey">Stable receiver registration.</param>
/// <param name="ProducerKey">Receiver-assigned producer namespace.</param>
/// <param name="MessageId">Retained delivery identity.</param>
/// <param name="ReceiptId">Observed receipt generation; retry rechecks it alongside Revision.</param>
/// <param name="MessageName">Wire payload contract alias.</param>
/// <param name="SchemaVersion">Wire payload version.</param>
/// <param name="Revision">Observed lifecycle revision; retry rechecks it.</param>
/// <param name="FailureCount">Committed failure observations in this receipt generation.</param>
/// <param name="FailuresSinceRedrive">Confirmed failures since the latest applied retry.</param>
/// <param name="ReceivedAt">Original database receipt time.</param>
/// <param name="DeadLetteredAt">Database time automatic processing stopped.</param>
/// <param name="LastFailureCode">Consumer-safe reason for the latest recorded failure.</param>
/// <param name="LastFailedAt">Database time of the latest recorded failure.</param>
/// <param name="RedriveCount">Applied operator retry count.</param>
/// <param name="LastRedrivenAt">Database time of the latest applied retry, if any.</param>
public sealed record InboxDeadLetter(
    string SubscriptionKey,
    string ProducerKey,
    Guid MessageId,
    Guid ReceiptId,
    string MessageName,
    int SchemaVersion,
    long Revision,
    long FailureCount,
    long FailuresSinceRedrive,
    DateTimeOffset ReceivedAt,
    DateTimeOffset DeadLetteredAt,
    string LastFailureCode,
    DateTimeOffset LastFailedAt,
    long RedriveCount,
    DateTimeOffset? LastRedrivenAt
);
