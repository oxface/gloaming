using Gloaming.Events.Serialization;
using ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;

namespace ModulithFoundry.Samples.Wholesale.Inventory;

// Finite demonstration setup only. The caller establishes tenancy and saves/commits.
// This is not an expected-version append implementation.
public static class InventoryHistorySeed
{
    public static void Add(
        InventoryDbContext database,
        Guid id,
        IReadOnlyList<(SerializedEvent Event, DateTimeOffset RecordedAt)> history
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfLessThan(history.Count, 1);
        string owner = database.RequiredOrganizationKey;
        var codec = StockPositionCodec.CreateCodec();
        IStockPositionEvent[] facts = history
            .Select(item =>
                codec.Deserialize(
                    item.Event.EventName,
                    item.Event.SchemaVersion,
                    item.Event.Payload
                )
            )
            .ToArray();
        var state = StockPositionEvolution.Evolve(null, facts);
        var main = StockPositionStateRow.FromState(
            owner,
            id,
            history.Count,
            history[^1].RecordedAt,
            state
        );
        database.Add(
            new EventStream
            {
                OrganizationKey = owner,
                Id = id,
                StreamType = StockPositionHistoryReader.StreamType,
                Version = history.Count,
                CreatedAt = history[0].RecordedAt,
                UpdatedAt = history[^1].RecordedAt,
            }
        );
        long version = 0;
        foreach (var (envelope, recordedAt) in history)
            database.Add(
                new StoredEvent
                {
                    OrganizationKey = owner,
                    EventId = Guid.NewGuid(),
                    StreamId = id,
                    StreamVersion = ++version,
                    EventName = envelope.EventName,
                    SchemaVersion = envelope.SchemaVersion,
                    Payload = envelope.Payload.Clone(),
                    RecordedAt = recordedAt,
                }
            );
        database.Add(main);
    }
}
