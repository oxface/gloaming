using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal sealed class PurchaseOrderQueries(
    PurchasingDbContext database,
    InlineStateReader<EventStream, PurchaseOrderStateRow> stateReader
) : IPurchaseOrderQueries
{
    public async Task<PurchaseOrderHistory?> ReadCurrentAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var current = await ReadAsync(id, cancellationToken);
        return current is not null
            ? current.ReadState().ToHistory(id, current.Version, current.RecordedAt)
            : null;
    }

    public async Task<PurchaseOrderSummary?> ReadSummaryAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var current = await ReadAsync(id, cancellationToken);
        if (current is null)
            return null;
        var state = current.ReadState();
        return new(
            id,
            current.Version,
            current.RecordedAt,
            state.Code,
            state.Currency,
            state.Lines.Count,
            state.Total
        );
    }

    private async Task<PurchaseOrderStateRow?> ReadAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        _ = database.RequiredOrganizationKey;
        var stream = await database
            .EventStreams.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == id && row.StreamType == PurchaseOrderHistoryReader.StreamType,
                cancellationToken
            );
        return stream is null ? null : await stateReader.ReadAsync(stream, cancellationToken);
    }
}
