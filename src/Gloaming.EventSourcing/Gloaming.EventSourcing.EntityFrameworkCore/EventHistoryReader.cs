using System.Linq.Expressions;
using Gloaming.Events.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Native complete-key prefix loading and integrity checks; consumers decode each event.</summary>
public abstract class EventHistoryReader<TEvent, TStreamRecord, TStoredEventRecord>
    : IEventHistoryReader<TEvent, TStreamRecord>
    where TEvent : class
    where TStreamRecord : class, IEventStreamRecord
    where TStoredEventRecord : class, IStoredEventRecord
{
    private readonly DbContext database;
    private readonly string streamType;
    private readonly IForeignKey streamForeignKey;

    /// <summary>Binds the native context and stable stream discriminator without selecting a serializer or reducer.</summary>
    protected EventHistoryReader(DbContext database, string streamType)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamType);

        var streamModel =
            database.Model.FindEntityType(typeof(TStreamRecord))
            ?? throw new InvalidOperationException("Map the stream record before reading history.");
        var eventModel =
            database.Model.FindEntityType(typeof(TStoredEventRecord))
            ?? throw new InvalidOperationException("Map the event record before reading history.");
        var streamKey =
            streamModel.FindPrimaryKey()
            ?? throw new InvalidOperationException("Map the complete stream primary key.");
        streamForeignKey = eventModel
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalKey == streamKey);

        foreach (var property in streamKey.Properties.Concat(streamForeignKey.Properties))
        {
            if (
                property.PropertyInfo is null
                || property.GetValueConverter() is not null
                || property.GetProviderClrType() is not null
            )
                throw new InvalidOperationException("Use mapped unconverted stream/event keys.");
        }

        this.database = database;
        this.streamType = streamType;
    }

    /// <summary>Decodes the concrete payload using consumer-owned aliases, schemas and serialization.</summary>
    protected abstract TEvent DecodeEvent(TStoredEventRecord record);

    /// <summary>Reads positions 1 through the requested version, bounded by the observed stream.</summary>
    public async Task<IReadOnlyList<ReplayedEvent<TEvent>>> ReadAsync(
        TStreamRecord observedStream,
        long? throughVersion = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(observedStream);
        cancellationToken.ThrowIfCancellationRequested();
        EventStreamValidation.ValidateExistingStream(observedStream, streamType);

        // Capture the bound and endpoints before querying. Do not discover a newer stream version.
        long observedVersion = observedStream.Version;
        long targetVersion = throughVersion ?? observedVersion;
        DateTimeOffset createdAt = observedStream.CreatedAt;
        DateTimeOffset updatedAt = observedStream.UpdatedAt;
        if (targetVersion < 1 || targetVersion > observedVersion)
            throw new ArgumentOutOfRangeException(nameof(throughVersion));

        var rows = await database
            .Set<TStoredEventRecord>()
            .AsNoTracking()
            .Where(MatchesStream(observedStream))
            .Where(record => record.StreamVersion <= targetVersion)
            .OrderBy(record => record.StreamVersion)
            .ToArrayAsync(cancellationToken);

        // Validate metadata before decoding any payload. Never conceal a missing tail.
        EventHistory.ValidateRange(
            rows.Select(record => new HistoryPosition(record.StreamVersion, record.RecordedAt)),
            0,
            targetVersion
        );
        if (
            rows.Any(record =>
                record.RecordedAt.Offset != TimeSpan.Zero
                || record.RecordedAt < createdAt
                || record.RecordedAt > updatedAt
            )
            || !rows[0].RecordedAt.EqualsExact(createdAt)
            || (targetVersion == observedVersion && !rows[^1].RecordedAt.EqualsExact(updatedAt))
        )
            throw new InvalidDataException("History timestamps differ from the observed stream.");

        var events = new ReplayedEvent<TEvent>[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = rows[index];
            var decodedEvent =
                DecodeEvent(record)
                ?? throw new InvalidDataException("An event decoder returned null.");
            events[index] = new(record.StreamVersion, record.RecordedAt, decodedEvent);
        }

        return events;
    }

    private Expression<Func<TStoredEventRecord, bool>> MatchesStream(TStreamRecord observedStream)
    {
        var keyValues = streamForeignKey
            .PrincipalKey.Properties.Select(property =>
                property.PropertyInfo!.GetValue(observedStream)
            )
            .ToArray();
        if (keyValues.Any(value => value is null))
            throw new ArgumentException("Supply the complete observed stream key.");

        return StreamKeyPredicate.Create<TStoredEventRecord>(
            streamForeignKey.Properties,
            keyValues
        );
    }
}
