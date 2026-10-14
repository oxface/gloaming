namespace ModulithFoundry.Samples.Wholesale.Sales.Contracts;

// Changed is proposed state. The caller owns the native Sales transaction, save and commit.
public interface IDraftOrderCommands
{
    Task<DraftOrderChangeResult> CreateAsync(
        CreateDraftOrder request,
        CancellationToken cancellationToken
    );

    Task<DraftOrderChangeResult> ChangeLinesAsync(
        ChangeDraftOrderLines request,
        CancellationToken cancellationToken
    );
}

public sealed record CreateDraftOrder(
    Guid Id,
    Guid CustomerId,
    string Code,
    string Currency,
    decimal TotalLimit,
    IReadOnlyList<DraftOrderLine> Lines
);

public sealed record ChangeDraftOrderLines(
    Guid Id,
    long ExpectedVersion,
    IReadOnlyList<DraftOrderLineChange> Changes
);

public abstract record DraftOrderLineChange
{
    public sealed record Add(DraftOrderLine Line) : DraftOrderLineChange;

    public sealed record Change(Guid LineId, decimal Quantity, decimal UnitPrice)
        : DraftOrderLineChange;

    public sealed record Remove(Guid LineId) : DraftOrderLineChange;
}

public abstract record DraftOrderChangeResult
{
    public sealed record Changed(DraftOrder Proposed) : DraftOrderChangeResult;

    public sealed record NotFound : DraftOrderChangeResult;

    public sealed record Conflict : DraftOrderChangeResult;

    public sealed record CustomerNotFound : DraftOrderChangeResult;

    public sealed record UnknownSku(string Sku) : DraftOrderChangeResult;

    public sealed record LimitExceeded(decimal Limit, decimal Requested) : DraftOrderChangeResult;
}
