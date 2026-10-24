namespace Gloaming.Samples.Wholesale.Sales.Contracts;

/// <summary>Sales-owned progress for an independently committed Inventory stock issue.</summary>
public interface IStockIssueRequests
{
    /// <summary>Commits a request and its command together. Identical retries preserve command identity.</summary>
    /// <remarks>Deadlines use UTC microsecond precision. Changed inputs under an existing request ID are rejected. Concurrent starts may require a fresh-scope retry.</remarks>
    Task<StockIssueRequest> StartAsync(
        StartStockIssueRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>Reads persisted progress in the established Organization without repairing it.</summary>
    Task<StockIssueRequest?> ReadAsync(Guid requestId, CancellationToken cancellationToken);
}

public sealed record StartStockIssueRequest(
    Guid RequestId,
    Guid StockPositionId,
    long ExpectedStockVersion,
    decimal Quantity,
    DateTimeOffset ReplyDeadline
);

public sealed record StockIssueRequest(
    Guid RequestId,
    Guid CommandMessageId,
    Guid StockPositionId,
    long ExpectedStockVersion,
    decimal Quantity,
    DateTimeOffset ReplyDeadline,
    StockIssueRequestStatus Status,
    long Version,
    long? RecordedStockVersion,
    decimal? RemainingQuantity,
    StockIssueRefusal? Refusal
);

public enum StockIssueRequestStatus
{
    AwaitingReply = 1,
    NeedsAttention = 2,
    Issued = 3,
    Declined = 4,
}

public enum StockIssueRefusal
{
    NotFound = 1,
    Conflict = 2,
    InsufficientStock = 3,
}
