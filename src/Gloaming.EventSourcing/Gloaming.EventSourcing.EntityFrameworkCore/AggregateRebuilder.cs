using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Explicit full replay into one inline aggregate. Native save/commit remain caller-owned.</summary>
public abstract class AggregateRebuilder<TAggregate, TEvent, TStreamRecord, TInlineStateRecord>
    : IAggregateRebuilder<TAggregate>
    where TAggregate : class, IEventSourcedAggregate<TEvent>
    where TEvent : class
    where TStreamRecord : class, IEventStreamRecord
    where TInlineStateRecord : class, IInlineStateRecord
{
    private readonly DbContext database;
    private readonly string streamType;
    private readonly IEventHistoryReader<TEvent, TStreamRecord> historyReader;
    private readonly AggregateStateMapping<TAggregate, TInlineStateRecord> stateMapping;
    private readonly InlineStateReader<TStreamRecord, TInlineStateRecord> reader;
    private readonly InlineStateWriter<TStreamRecord, TInlineStateRecord> writer;

    protected AggregateRebuilder(
        DbContext database,
        string streamType,
        AggregateStateMapping<TAggregate, TInlineStateRecord> stateMapping,
        IEventHistoryReader<TEvent, TStreamRecord> historyReader
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(stateMapping);
        ArgumentNullException.ThrowIfNull(historyReader);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamType);
        if (
            !RequiredInlineStateExtensions.IsStateRegistered(
                database.Model,
                typeof(TStreamRecord),
                typeof(TInlineStateRecord),
                streamType
            )
        )
            throw new InvalidOperationException(
                "Register the aggregate state in the native model/save contract."
            );
        this.database = database;
        this.streamType = streamType;
        this.stateMapping = stateMapping;
        this.historyReader = historyReader;
        reader = new(database);
        writer = new(database);
    }

    protected abstract TAggregate Rehydrate(
        TStreamRecord observedStream,
        IReadOnlyList<TEvent> events
    );

    public async Task<AggregateRebuildResult?> RebuildAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        var transaction =
            database.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Rebuilding requires an explicit native transaction."
            );
        if (
            database.ChangeTracker.Entries().Any()
            || RequiredInlineStateExtensions.HasMaintenanceWrite(database)
        )
            throw new InvalidOperationException("Rebuild once in a fresh operation context.");

        var observedStream = await database
            .Set<TStreamRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(streamRecord => streamRecord.Id == id, cancellationToken);
        if (observedStream is null)
            return null;

        EventStreamValidation.ValidateExistingStream(observedStream, streamType);
        // Validate the captured prefix before consumer evolution sees any facts.
        var replayedEvents = (
            await historyReader.ReadAsync(observedStream, cancellationToken: cancellationToken)
        ).ToArray();
        if (replayedEvents.LongLength != observedStream.Version)
            throw new InvalidDataException("Replay must include the complete captured prefix.");

        var previousRecordedAt = observedStream.CreatedAt;
        for (int index = 0; index < replayedEvents.Length; index++)
        {
            var replayedEvent = replayedEvents[index];
            ArgumentNullException.ThrowIfNull(replayedEvent);
            ArgumentNullException.ThrowIfNull(replayedEvent.Event);
            if (
                replayedEvent.StreamVersion != index + 1L
                || replayedEvent.RecordedAt.Offset != TimeSpan.Zero
                || replayedEvent.RecordedAt < previousRecordedAt
                || replayedEvent.RecordedAt > observedStream.UpdatedAt
            )
                throw new InvalidDataException(
                    "Replay positions and UTC times must match the captured prefix."
                );
            previousRecordedAt = replayedEvent.RecordedAt;
        }

        if (
            !replayedEvents[0].RecordedAt.EqualsExact(observedStream.CreatedAt)
            || !previousRecordedAt.EqualsExact(observedStream.UpdatedAt)
        )
            throw new InvalidDataException(
                "Replay endpoints must match the observed stream record."
            );

        // Reuse historical evolution, without producing pending events or re-running command eligibility.
        var domainEvents = replayedEvents.Select(replayedEvent => replayedEvent.Event).ToArray();
        var rebuiltAggregate = Rehydrate(observedStream, domainEvents);
        if (
            rebuiltAggregate is null
            || rebuiltAggregate.Id != id
            || rebuiltAggregate.ExpectedVersion != observedStream.Version
            || rebuiltAggregate.Version != observedStream.Version
            || rebuiltAggregate.PendingEvents.Count != 0
        )
            throw new InvalidDataException(
                "Rebuilt aggregate must represent exactly the captured head without pending facts."
            );

        // Observe row metadata only: repairing an unreadable body must not require decoding that body.
        var persistedStateRow = await reader.FindAsync(observedStream, cancellationToken);
        if (
            persistedStateRow is IInlineStateRecord stateRow
            && stateRow.Version > observedStream.Version
        )
        {
            // Read committed can observe an ordinary append between our header and state reads.
            var latest = await database
                .Set<TStreamRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(streamRecord => streamRecord.Id == id, cancellationToken);
            if (latest is not null)
            {
                EventStreamValidation.ValidateExistingStream(latest, streamType);
                var key = database.Model.FindEntityType(typeof(TStreamRecord))!.FindPrimaryKey()!;
                if (
                    latest.Version > observedStream.Version
                    && latest.Version >= stateRow.Version
                    && latest.ConcurrencyStamp != observedStream.ConcurrencyStamp
                    && latest.CreatedAt.EqualsExact(observedStream.CreatedAt)
                    && key.Properties.All(property =>
                        Equals(
                            property.PropertyInfo!.GetValue(latest),
                            property.PropertyInfo!.GetValue(observedStream)
                        )
                    )
                )
                    throw new AggregateCaptureConflict(database, id, transaction);
            }

            throw new InvalidDataException("Rebuild state cannot be ahead of its captured stream.");
        }

        var candidate = stateMapping.ToRow(rebuiltAggregate);
        writer.PopulateMetadata(
            observedStream,
            persistedStateRow,
            candidate,
            observedStream.Version,
            observedStream.UpdatedAt
        );
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(transaction, database.Database.CurrentTransaction))
            throw new InvalidOperationException("The native transaction changed during replay.");

        // A same-version repair must invalidate writers which observed the old state.
        database.Set<TStreamRecord>().Attach(observedStream);
        observedStream.ConcurrencyStamp = Guid.NewGuid();
        var state = writer.AddOrUpdate(persistedStateRow, candidate);
        RequiredInlineStateExtensions.RecordMaintenanceWrite(
            database,
            observedStream,
            state,
            transaction
        );
        return new(observedStream.Version, observedStream.UpdatedAt);
    }
}

// Only the provided rebuilder can identify a verified stale capture before a native save.
internal sealed class AggregateCaptureConflict(
    DbContext database,
    Guid id,
    IDbContextTransaction transaction
) : DbUpdateConcurrencyException("The stream advanced between captured header and state reads.")
{
    internal bool Matches(DbContext candidate, Guid target, IDbContextTransaction current) =>
        ReferenceEquals(database, candidate)
        && id == target
        && ReferenceEquals(transaction, current);
}
