using System.Text.Json;

namespace ModulithFoundry.Samples.EventStorageDemo;

// Finite authored setup, not an append or business-command API.
public static class DemoData
{
    public static readonly Guid CounterId = Guid.Parse("e5030000-0000-0000-0000-000000000001");
    public static readonly Guid NoteId = Guid.Parse("e5030000-0000-0000-0000-000000000002");
    public static DateTimeOffset RecordedAt { get; } = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    public static void Stage(StorageDbContext database)
    {
        database.Streams.AddRange(
            new EventStreamRecord
            {
                Id = CounterId,
                StreamType = "proof.counter",
                Version = 2,
                CreatedAt = RecordedAt,
                UpdatedAt = RecordedAt,
                Description = "Independent counter",
            },
            new EventStreamRecord
            {
                Id = NoteId,
                StreamType = "proof.note",
                Version = 1,
                CreatedAt = RecordedAt,
                UpdatedAt = RecordedAt,
                Description = "Independent note",
            }
        );
        database.Events.AddRange(
            Event(
                CounterId,
                1,
                "proof.counter-started",
                JsonSerializer.SerializeToElement(new { value = 10 })
            ),
            Event(
                CounterId,
                2,
                "proof.counter-increased",
                JsonSerializer.SerializeToElement(new { amount = 7 })
            ),
            Event(
                NoteId,
                1,
                "proof.note-created",
                JsonSerializer.SerializeToElement(new { text = "review" })
            )
        );
    }

    public static StoredEventRecord Event(
        Guid stream,
        long version,
        string name,
        JsonElement payload
    ) =>
        new()
        {
            EventId = Guid.NewGuid(),
            StreamId = stream,
            StreamVersion = version,
            EventName = name,
            SchemaVersion = 1,
            RecordedAt = RecordedAt,
            Payload = payload,
        };
}
