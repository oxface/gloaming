using Microsoft.EntityFrameworkCore;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

internal sealed class InlineStateWriter<TStreamRecord, TInlineStateRecord>(DbContext database)
    where TStreamRecord : class, IEventStreamRecord
    where TInlineStateRecord : class, IInlineStateRecord
{
    private readonly Microsoft.EntityFrameworkCore.Metadata.IForeignKey streamForeignKey =
        InlineStateMetadata.StreamForeignKey(
            database.Model,
            typeof(TStreamRecord),
            typeof(TInlineStateRecord)
        );

    // Fill persistence metadata on a detached encoding before any unit-of-work mutation.
    internal void PopulateMetadata(
        TStreamRecord streamRecord,
        TInlineStateRecord? observedRow,
        TInlineStateRecord candidate,
        long version,
        DateTimeOffset recordedAt
    )
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (
            ReferenceEquals(observedRow, candidate)
            || database.Entry(candidate).State != EntityState.Detached
        )
            throw new InvalidOperationException("State mapping must return a fresh detached row.");
        for (int index = 0; index < streamForeignKey.Properties.Count; index++)
            streamForeignKey
                .Properties[index]
                .PropertyInfo!.SetValue(
                    candidate,
                    streamForeignKey
                        .PrincipalKey.Properties[index]
                        .PropertyInfo!.GetValue(streamRecord)
                );
        candidate.Version = version;
        candidate.RecordedAt = recordedAt;
        if (observedRow is not null && database.Entry(observedRow).State != EntityState.Detached)
            throw new InvalidOperationException("Use an untracked state observation.");
        var keyValues = streamForeignKey.Properties.Select(property =>
            property.PropertyInfo!.GetValue(candidate)
        );
        if (
            database
                .ChangeTracker.Entries<TInlineStateRecord>()
                .Any(entry =>
                    keyValues.SequenceEqual(
                        InlineStateMetadata.Values(entry, streamForeignKey.Properties)
                    )
                )
        )
            throw new InvalidOperationException(
                "The observed state row must not already be tracked."
            );
    }

    // Track encoded state; aggregate evolution has already happened in consumer domain code.
    internal TInlineStateRecord AddOrUpdate(
        TInlineStateRecord? observedRow,
        TInlineStateRecord candidate
    )
    {
        if (observedRow is null)
        {
            database.Set<TInlineStateRecord>().Add(candidate);
            return candidate;
        }
        // Attach the observation first so EF retains the version used by the native UPDATE predicate.
        database.Set<TInlineStateRecord>().Attach(observedRow);
        database.Entry(observedRow).CurrentValues.SetValues(candidate);
        return observedRow;
    }
}

// Keep the state-record type internal to the configured mapping rather than adding
// another generic argument to EventStore. The concrete binding owns all object casts.
internal abstract class InlineAggregatePersistence<TAggregate, TStreamRecord>
    where TAggregate : class
    where TStreamRecord : class, IEventStreamRecord
{
    internal abstract Task<object> ReadAsync(TStreamRecord streamRecord, CancellationToken token);
    internal abstract TAggregate ToAggregate(object state);
    internal abstract object Encode(
        TStreamRecord streamRecord,
        object? observedRow,
        TAggregate aggregate,
        long version,
        DateTimeOffset recordedAt
    );
    internal abstract void AddOrUpdate(object? observedRow, object candidate);
}

internal sealed class InlineAggregatePersistence<TAggregate, TStreamRecord, TInlineStateRecord>(
    DbContext database,
    AggregateStateMapping<TAggregate, TInlineStateRecord> mapping
) : InlineAggregatePersistence<TAggregate, TStreamRecord>
    where TAggregate : class
    where TStreamRecord : class, IEventStreamRecord
    where TInlineStateRecord : class, IInlineStateRecord
{
    private readonly InlineStateReader<TStreamRecord, TInlineStateRecord> reader = new(database);
    private readonly InlineStateWriter<TStreamRecord, TInlineStateRecord> writer = new(database);

    internal override async Task<object> ReadAsync(
        TStreamRecord streamRecord,
        CancellationToken token
    ) => await reader.ReadAsync(streamRecord, token);

    internal override TAggregate ToAggregate(object state) =>
        mapping.ToAggregate((TInlineStateRecord)state);

    internal override object Encode(
        TStreamRecord streamRecord,
        object? observedRow,
        TAggregate aggregate,
        long version,
        DateTimeOffset recordedAt
    )
    {
        var candidate = mapping.ToRow(aggregate);
        writer.PopulateMetadata(
            streamRecord,
            (TInlineStateRecord?)observedRow,
            candidate,
            version,
            recordedAt
        );
        return candidate;
    }

    internal override void AddOrUpdate(object? observedRow, object candidate) =>
        writer.AddOrUpdate((TInlineStateRecord?)observedRow, (TInlineStateRecord)candidate);
}
