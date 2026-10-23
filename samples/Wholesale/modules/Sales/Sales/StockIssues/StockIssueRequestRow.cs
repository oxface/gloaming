using Gloaming.Samples.Wholesale.Sales.Contracts;

namespace Gloaming.Samples.Wholesale.Sales.StockIssues;

internal sealed class StockIssueRequestRow
{
    public string OrganizationKey { get; private set; } = null!;
    public Guid Id { get; private set; }
    public Guid CommandMessageId { get; private set; }
    public Guid StockPositionId { get; private set; }
    public long ExpectedStockVersion { get; private set; }
    public decimal Quantity { get; private set; }
    public DateTimeOffset ReplyDeadline { get; private set; }
    public StockIssueRequestStatus Status { get; private set; }
    public long Version { get; private set; }
    public long? RecordedStockVersion { get; private set; }
    public decimal? RemainingQuantity { get; private set; }
    public DateTimeOffset? RecordedAt { get; private set; }
    public StockIssueRefusal? Refusal { get; private set; }
    public decimal? AvailableQuantity { get; private set; }
    public decimal? RequestedQuantity { get; private set; }

    internal static StockIssueRequestRow Create(
        string organization,
        StartStockIssueRequest request
    ) =>
        new()
        {
            OrganizationKey = organization,
            Id = request.RequestId,
            CommandMessageId = Guid.NewGuid(),
            StockPositionId = request.StockPositionId,
            ExpectedStockVersion = request.ExpectedStockVersion,
            Quantity = request.Quantity,
            ReplyDeadline = AtDatabasePrecision(request.ReplyDeadline),
            Status = StockIssueRequestStatus.AwaitingReply,
            Version = 1,
        };

    internal void ValidateRetry(StartStockIssueRequest request)
    {
        if (
            StockPositionId != request.StockPositionId
            || ExpectedStockVersion != request.ExpectedStockVersion
            || Quantity != request.Quantity
            || ReplyDeadline != AtDatabasePrecision(request.ReplyDeadline)
        )
            throw new InvalidOperationException(
                "An existing stock-issue request cannot be reused with different inputs."
            );
    }

    // PostgreSQL timestamps retain microseconds, whereas .NET can carry 100-nanosecond ticks.
    // Normalize before persistence and semantic comparison so identical retries remain identical.
    internal static DateTimeOffset AtDatabasePrecision(DateTimeOffset timestamp) =>
        new(timestamp.UtcTicks - timestamp.UtcTicks % 10, TimeSpan.Zero);

    internal void MarkOverdue()
    {
        if (Status != StockIssueRequestStatus.AwaitingReply)
            return;

        Status = StockIssueRequestStatus.NeedsAttention;
        Version = checked(Version + 1);
    }

    internal void Resolve(StockIssueOutcome outcome)
    {
        if (Status is StockIssueRequestStatus.Issued or StockIssueRequestStatus.Declined)
        {
            var accepted = new StockIssueOutcome(
                RecordedStockVersion,
                RemainingQuantity,
                RecordedAt,
                Refusal,
                AvailableQuantity,
                RequestedQuantity
            );
            if (accepted != outcome)
                throw new InvalidDataException(
                    "A conflicting stock-issue outcome requires investigation."
                );

            return;
        }

        RecordedStockVersion = outcome.StockVersion;
        RemainingQuantity = outcome.Remaining;
        RecordedAt = outcome.RecordedAt;
        Refusal = outcome.Refusal;
        AvailableQuantity = outcome.Available;
        RequestedQuantity = outcome.Requested;
        Status = outcome.Refusal is null
            ? StockIssueRequestStatus.Issued
            : StockIssueRequestStatus.Declined;
        Version = checked(Version + 1);
    }

    internal StockIssueRequest Read() =>
        new(
            Id,
            CommandMessageId,
            StockPositionId,
            ExpectedStockVersion,
            Quantity,
            ReplyDeadline,
            Status,
            Version,
            RecordedStockVersion,
            RemainingQuantity,
            Refusal
        );
}

internal sealed record StockIssueOutcome(
    long? StockVersion,
    decimal? Remaining,
    DateTimeOffset? RecordedAt,
    StockIssueRefusal? Refusal,
    decimal? Available,
    decimal? Requested
);
