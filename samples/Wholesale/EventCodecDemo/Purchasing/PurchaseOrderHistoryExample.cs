using Gloaming.Events.History;
using Gloaming.Events.Serialization;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

internal static class PurchaseOrderHistoryExample
{
    internal static readonly DateTimeOffset DraftedAt = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset LinesRecordedAt = DraftedAt.AddSeconds(10);

    internal static RecordedEvent[] Load(string fixtureDirectory) =>
        [
            new(
                1,
                DraftedAt,
                FixtureEvents.ReadEnvelope(
                    Path.Combine(fixtureDirectory, "Purchasing", "drafted.v1.json")
                )
            ),
            new(
                2,
                LinesRecordedAt,
                FixtureEvents.ReadEnvelope(
                    Path.Combine(fixtureDirectory, "Purchasing", "line-set.v1.json")
                )
            ),
            new(
                3,
                LinesRecordedAt,
                FixtureEvents.ReadEnvelope(
                    Path.Combine(fixtureDirectory, "Purchasing", "line-replaced.v1.json")
                )
            ),
        ];

    internal static PurchaseOrderSummary? ReadAtVersion(
        JsonEventCodec<IPurchaseOrderEvent> codec,
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

    internal static PurchaseOrderSummary? ReadAsOf(
        JsonEventCodec<IPurchaseOrderEvent> codec,
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

    private static PurchaseOrderSummary? ReadPrefix(
        JsonEventCodec<IPurchaseOrderEvent> codec,
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
            : PurchaseOrderExample.Read(
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
