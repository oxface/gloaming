namespace Gloaming.Samples.Wholesale.Inventory.Contracts;

/// <summary>Consumer-owned notification of one accepted stock-issue command, not a domain-event base.</summary>
public sealed record StockIssueRecordedV1(
    Guid MessageId,
    string OrganizationKey,
    Guid StockPositionId,
    long Version,
    decimal IssuedQuantity,
    decimal RemainingQuantity,
    DateTimeOffset RecordedAt
);
