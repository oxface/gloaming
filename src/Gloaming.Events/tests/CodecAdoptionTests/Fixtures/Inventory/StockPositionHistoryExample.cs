using Gloaming.Events.History;
using Gloaming.Events.Serialization;

namespace Gloaming.Samples.Wholesale.EventCodecDemo.Inventory;

internal static class StockPositionHistoryExample
{
    internal static readonly DateTimeOffset OpenedAt = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset FirstReceiptAt = OpenedAt.AddSeconds(10);

    internal static RecordedEvent[] Load(string fixtureDirectory) =>
        [
            new(
                1,
                OpenedAt,
                FixtureEvents.ReadEnvelope(
                    Path.Combine(fixtureDirectory, "Inventory", "opened.v1.json")
                )
            ),
            new(
                2,
                FirstReceiptAt,
                FixtureEvents.ReadEnvelope(
                    Path.Combine(fixtureDirectory, "Inventory", "received.v1.json")
                )
            ),
            new(
                3,
                FirstReceiptAt.AddSeconds(10),
                FixtureEvents.ReadEnvelope(
                    Path.Combine(fixtureDirectory, "Inventory", "received-second.v1.json")
                )
            ),
        ];

    internal static StockPositionSummary? ReadAtVersion(
        JsonEventCodec<IStockPositionEvent> codec,
        IReadOnlyList<RecordedEvent> rows,
        long capturedHead,
        long version
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capturedHead, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(version, capturedHead);
        return ReadPrefix(codec, rows, version);
    }

    internal static StockPositionSummary? ReadAsOf(
        JsonEventCodec<IStockPositionEvent> codec,
        IReadOnlyList<RecordedEvent> rows,
        long capturedHead,
        DateTimeOffset recordedAt
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capturedHead, 1);
        long throughVersion = rows.Where(row =>
                row.StreamVersion <= capturedHead && row.RecordedAt <= recordedAt
            )
            .Select(row => row.StreamVersion)
            .DefaultIfEmpty()
            .Max();
        return ReadPrefix(codec, rows, throughVersion);
    }

    private static StockPositionSummary? ReadPrefix(
        JsonEventCodec<IStockPositionEvent> codec,
        IEnumerable<RecordedEvent> rows,
        long throughVersion
    )
    {
        RecordedEvent[] selected = rows.Where(row => row.StreamVersion <= throughVersion)
            .OrderBy(row => row.StreamVersion)
            .ToArray();
        EventHistory.ValidateRange(
            selected.Select(row => new HistoryPosition(row.StreamVersion, row.RecordedAt)),
            afterVersion: 0,
            throughVersion
        );
        return selected.Length == 0
            ? null
            : StockPositionExample.Read(
                selected.Select(row =>
                    codec.Deserialize(
                        row.Event.EventName,
                        row.Event.SchemaVersion,
                        row.Event.Payload
                    )
                )
            );
    }
}
