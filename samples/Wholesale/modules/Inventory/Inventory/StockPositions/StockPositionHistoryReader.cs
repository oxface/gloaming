using Gloaming.Events.Serialization;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

internal sealed class StockPositionHistoryReader(InventoryDbContext database)
    : EventHistoryReader<IStockPositionEvent, EventStream, StoredEvent>(database, StreamType),
        IStockPositionHistory
{
    internal const string StreamType = "inventory.stock-position";
    private static readonly JsonEventCodec<IStockPositionEvent> Codec =
        StockPositionCodec.CreateCodec();

    public Task<StockPositionHistory?> ReadCurrentAsync(
        Guid id,
        CancellationToken cancellationToken
    ) => ReadAsync(id, null, null, cancellationToken);

    public Task<StockPositionHistory?> ReadAtVersionAsync(
        Guid id,
        long version,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        return ReadAsync(id, version, null, cancellationToken);
    }

    public Task<StockPositionHistory?> ReadAsOfAsync(
        Guid id,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    ) => ReadAsync(id, null, recordedAt.ToUniversalTime(), cancellationToken);

    private async Task<StockPositionHistory?> ReadAsync(
        Guid id,
        long? version,
        DateTimeOffset? cutoff,
        CancellationToken cancellationToken
    )
    {
        _ = database.RequiredOrganizationKey;
        EventStream? stream = await database
            .EventStreams.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == id && row.StreamType == StreamType,
                cancellationToken
            );
        if (stream is null)
            return null;
        if (stream.Version < 1 || stream.CreatedAt > stream.UpdatedAt)
            throw new InvalidDataException("The stock position stream header is invalid.");
        if (version > stream.Version)
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "The requested version exceeds the captured head."
            );

        // Keep this observed head even if another transaction commits before the event query.
        long target = version ?? stream.Version;
        if (cutoff is { } boundary && boundary < stream.UpdatedAt)
        {
            if (boundary < stream.CreatedAt)
                return null;
            target =
                await database
                    .Events.AsNoTracking()
                    .Where(row =>
                        row.StreamId == id
                        && row.StreamVersion <= stream.Version
                        && row.RecordedAt <= boundary
                    )
                    .Select(row => (long?)row.StreamVersion)
                    .MaxAsync(cancellationToken)
                ?? 0;
            if (target == 0)
                throw new InvalidDataException("The stock position creation event is missing.");
        }
        var events = await base.ReadAsync(stream, target, cancellationToken);
        return StockPositionEvolution.Rehydrate(
            id,
            target,
            events[^1].RecordedAt,
            events.Select(item => item.Event)
        );
    }

    protected override IStockPositionEvent DecodeEvent(StoredEvent record) =>
        Codec.Deserialize(record.EventName, record.SchemaVersion, record.Payload);
}
