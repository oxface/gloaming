namespace Gloaming.Samples.Wholesale.Inventory.Contracts;

/// <summary>Handled business refusal; no stock facts were appended.</summary>
public sealed record StockIssueDeclinedV1(
    Guid MessageId,
    string OrganizationKey,
    Guid StockPositionId,
    long ExpectedVersion,
    StockIssueDeclineReason Reason,
    decimal? Available,
    decimal? Requested
);

public enum StockIssueDeclineReason
{
    NotFound = 1,
    Conflict = 2,
    InsufficientStock = 3,
}
