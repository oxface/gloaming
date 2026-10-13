using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using Rootbolt.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

internal sealed class StockPositionQueries(
    InventoryDbContext database,
    InlineStateReader<EventStream, StockPositionStateRow> stateReader
) : IStockPositionQueries
{
    public async Task<IReadOnlyList<StockPositionHistory>> ReadAvailableAsync(
        decimal requiredQuantity,
        CancellationToken cancellationToken
    )
    {
        _ = database.RequiredOrganizationKey;
        var rows = await database
            .StockPositions.AsNoTracking()
            .WithAvailableAtLeast(requiredQuantity)
            .Join(
                database
                    .EventStreams.AsNoTracking()
                    .Where(stream => stream.StreamType == StockPositionHistoryReader.StreamType),
                state => new { state.OrganizationKey, Id = state.StreamId },
                stream => new { stream.OrganizationKey, stream.Id },
                (state, stream) => new { State = state, Stream = stream }
            )
            .OrderBy(row => row.State.StreamId)
            .ToArrayAsync(cancellationToken);
        var mapping = new InlineStateReader<EventStream, StockPositionStateRow>(database);
        return rows.Select(row =>
            {
                mapping.Validate(row.Stream, row.State);
                return row
                    .State.ReadState()
                    .ToHistory(row.State.StreamId, row.State.Version, row.State.RecordedAt);
            })
            .ToArray();
    }

    public async Task<StockPositionHistory?> ReadCurrentAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        _ = database.RequiredOrganizationKey;
        var stream = await database
            .EventStreams.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == id && row.StreamType == StockPositionHistoryReader.StreamType,
                cancellationToken
            );
        if (stream is null)
            return null;
        var current = await stateReader.ReadAsync(stream, cancellationToken);
        return current.ReadState().ToHistory(id, current.Version, current.RecordedAt);
    }
}
