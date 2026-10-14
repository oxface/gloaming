using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

internal sealed class EventBatchEncoder<TEvent, TStreamRecord, TStoredEventRecord>
    where TEvent : class
    where TStreamRecord : class, IEventStreamRecord
    where TStoredEventRecord : class, IStoredEventRecord
{
    private readonly DbContext database;
    private readonly EventRecordMapping<TEvent, TStreamRecord, TStoredEventRecord> mapping;
    private readonly TimeProvider clock;

    internal EventBatchEncoder(
        DbContext database,
        EventRecordMapping<TEvent, TStreamRecord, TStoredEventRecord> mapping,
        TimeProvider clock
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapping.StreamType);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mapping.StreamType.Length, 100);
        this.database = database;
        this.mapping = mapping;
        this.clock = clock;
    }

    internal EncodedBatch<TStoredEventRecord> Encode(
        TStreamRecord streamRecord,
        IEventSourcedAggregate<TEvent> aggregate,
        CancellationToken token
    )
    {
        if (
            aggregate.Id != streamRecord.Id
            || streamRecord.StreamType != mapping.StreamType
            || aggregate.ExpectedVersion != streamRecord.Version
        )
            throw new DbUpdateConcurrencyException(
                "The aggregate must match its observed stream identity, stream type and version."
            );

        var events = aggregate.PendingEvents.ToArray();
        long nextVersion = checked(streamRecord.Version + events.Length);
        if (events.Length == 0 || aggregate.Version != nextVersion)
            throw new InvalidOperationException(
                "The aggregate version must include exactly its pending facts."
            );

        DateTimeOffset recordedAt = clock.GetUtcNow();
        if (
            recordedAt.Offset != TimeSpan.Zero
            || (streamRecord.Version > 0 && recordedAt < streamRecord.UpdatedAt)
        )
            throw new InvalidOperationException("Recorded UTC time cannot regress.");

        // Resolve the actual mapped relationship, including any consumer-owned composite key.
        var streamModel = database.Model.FindEntityType(typeof(TStreamRecord))!;
        if (
            streamModel.FindProperty(nameof(IEventStreamRecord.Version))
                is not {
                    IsConcurrencyToken: true,
                    ValueGenerated: Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never
                }
            || streamModel.FindProperty(nameof(IEventStreamRecord.ConcurrencyStamp))
                is not {
                    IsConcurrencyToken: true,
                    ValueGenerated: Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never
                }
        )
            throw new InvalidOperationException(
                "Map the native stream version and concurrency stamp tokens."
            );
        var streamKey = streamModel.FindPrimaryKey()!;
        var eventModel = database.Model.FindEntityType(typeof(TStoredEventRecord))!;
        var streamForeignKey = eventModel
            .GetForeignKeys()
            .Single(key => key.PrincipalKey == streamKey);
        object?[] keyValues = streamKey
            .Properties.Select(property => property.PropertyInfo!.GetValue(streamRecord))
            .ToArray();
        if (keyValues.Any(value => value is null))
            throw new ArgumentException("Supply the complete stream primary key.");
        if (
            database
                .ChangeTracker.Entries<TStreamRecord>()
                .Any(entry =>
                    keyValues.SequenceEqual(InlineStateMetadata.Values(entry, streamKey.Properties))
                )
        )
            throw new InvalidOperationException("Use an untracked stream observation once.");

        var rows = new List<TStoredEventRecord>();
        var uniqueRows = new HashSet<TStoredEventRecord>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < events.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(events[index]);

            var row = mapping.ToRow(events[index], streamRecord);
            ArgumentNullException.ThrowIfNull(row);
            if (!uniqueRows.Add(row) || database.Entry(row).State != EntityState.Detached)
                throw new InvalidOperationException(
                    "Event mapping must return a fresh detached row for each fact."
                );

            ArgumentException.ThrowIfNullOrWhiteSpace(row.EventName);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(row.EventName.Length, 200);
            ArgumentOutOfRangeException.ThrowIfLessThan(row.SchemaVersion, 1);
            if (row.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                throw new ArgumentException("Supply a nonnull JSON event payload.");

            row.EventId = Guid.NewGuid();
            row.StreamId = streamRecord.Id;
            row.StreamVersion = checked(streamRecord.Version + index + 1);
            row.RecordedAt = recordedAt;
            // Own the JSON lifetime independently of the mapping's originating JsonDocument.
            row.Payload = row.Payload.Clone();

            if (
                !keyValues.SequenceEqual(
                    streamForeignKey.Properties.Select(property =>
                        property.PropertyInfo!.GetValue(row)
                    )
                )
            )
                throw new ArgumentException("The envelope must match the complete stream key.");

            rows.Add(row);
        }

        return new(rows.ToArray(), nextVersion, recordedAt);
    }
}

internal sealed record EncodedBatch<TStoredEventRecord>(
    TStoredEventRecord[] Rows,
    long NextVersion,
    DateTimeOffset RecordedAt
);
