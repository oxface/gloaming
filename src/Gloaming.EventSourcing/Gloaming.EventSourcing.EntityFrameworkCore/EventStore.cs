using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Native stream lookup, version checks, batch encoding and inline aggregate writes.</summary>
public abstract class EventStore<TAggregate, TEvent, TStreamRecord, TStoredEventRecord>
    : IEventStore<TAggregate>
    where TAggregate : class, IEventSourcedAggregate<TEvent>
    where TEvent : class
    where TStreamRecord : class, IEventStreamRecord
    where TStoredEventRecord : class, IStoredEventRecord
{
    private readonly DbContext database;
    private readonly EventBatchEncoder<TEvent, TStreamRecord, TStoredEventRecord> encoder;
    private readonly string streamType;
    private readonly Dictionary<Guid, StreamWriteObservation> loadedStreams = [];
    private InlineAggregatePersistence<TAggregate, TStreamRecord>? inlineStatePersistence;

    /// <summary>Binds the native context, event envelope mapping and application append clock; caller transaction/save/commit remain explicit.</summary>
    protected EventStore(
        DbContext database,
        EventRecordMapping<TEvent, TStreamRecord, TStoredEventRecord> records,
        TimeProvider timeProvider
    )
    {
        encoder = new(database, records, timeProvider);
        this.database = database;
        streamType = records.StreamType;
    }

    /// <summary>Supplies trusted ownership and extra fields only. The store assigns stream metadata.</summary>
    protected abstract TStreamRecord CreateStream(Guid id);

    /// <summary>A raw stream may reuse its existing history reader. Inline stores configure a mapping instead.</summary>
    protected virtual Task<TAggregate> LoadAggregateAsync(
        TStreamRecord stream,
        CancellationToken cancellationToken
    ) =>
        throw new InvalidOperationException("Configure aggregate state or a raw aggregate reader.");

    /// <summary>Selects the consumer mapping for the required transactionally current inline aggregate.</summary>
    protected void ConfigureInlineState<TInlineStateRecord>(
        AggregateStateMapping<TAggregate, TInlineStateRecord> mapping
    )
        where TInlineStateRecord : class, IInlineStateRecord
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (inlineStatePersistence is not null || loadedStreams.Count != 0)
            throw new InvalidOperationException(
                "Configure one aggregate state before using the store."
            );
        if (
            !RequiredInlineStateExtensions.IsRegistered(
                database.Model,
                typeof(TStreamRecord),
                typeof(TStoredEventRecord),
                typeof(TInlineStateRecord),
                streamType
            )
        )
            throw new InvalidOperationException(
                "Register this aggregate state in the native model/save contract."
            );
        inlineStatePersistence = new InlineAggregatePersistence<
            TAggregate,
            TStreamRecord,
            TInlineStateRecord
        >(database, mapping);
    }

    /// <summary>Loads an aggregate and its consistent observed stream version in the owning write transaction; optional expected version rejects stale callers.</summary>
    public async Task<TAggregate?> GetForWritingAsync(
        Guid id,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (expectedVersion is < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        var transaction = RequireTransaction();
        if (
            inlineStatePersistence is null
            && RequiredInlineStateExtensions.HasRegistration(
                database.Model,
                typeof(TStreamRecord),
                streamType
            )
        )
            throw new InvalidOperationException(
                "Configure the registered inline aggregate state before writing."
            );
        if (database.Set<TStreamRecord>().Local.Any(row => row.Id == id))
            throw new InvalidOperationException(
                "Use one terminal append per stream in a fresh context."
            );
        loadedStreams.Remove(id);
        TStreamRecord? stream = await database
            .Set<TStreamRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (stream is null)
        {
            stream = CreateStream(id);
            ArgumentNullException.ThrowIfNull(stream);
            stream.Id = id;
            stream.StreamType = streamType;
            if (stream.Version != 0 || database.Entry(stream).State != EntityState.Detached)
                throw new InvalidOperationException("Return a detached unstarted stream.");
            if (!ReferenceEquals(transaction, database.Database.CurrentTransaction))
                throw new InvalidOperationException(
                    "The native transaction changed during loading."
                );
            loadedStreams[id] = new(stream, null, transaction, expectedVersion, null);
            return null;
        }
        EventStreamValidation.ValidateExistingStream(stream, streamType);
        if (expectedVersion is { } expectation && expectation != stream.Version)
            throw new DbUpdateConcurrencyException(
                "The stream differs from the command's expected version."
            );
        object? state = inlineStatePersistence is null
            ? null
            : await inlineStatePersistence.ReadAsync(stream, cancellationToken);
        TAggregate aggregate = inlineStatePersistence is null
            ? await LoadAggregateAsync(stream, cancellationToken)
            : inlineStatePersistence.ToAggregate(state!);
        if (
            aggregate.Id != id
            || aggregate.ExpectedVersion != stream.Version
            || aggregate.Version != stream.Version
            || aggregate.PendingEvents.Count != 0
        )
            throw new InvalidDataException(
                "Reconstituted aggregate does not represent the captured stream version."
            );
        if (!ReferenceEquals(transaction, database.Database.CurrentTransaction))
            throw new InvalidOperationException("The native transaction changed during loading.");
        loadedStreams[id] = new(stream, aggregate, transaction, expectedVersion, state);
        return aggregate;
    }

    /// <summary>Proposes the aggregate accepted pending batch, stream metadata and required inline state against its observed version; does not save or commit.</summary>
    public Task<EventAppendResult> AppendAsync(
        TAggregate aggregate,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !loadedStreams.TryGetValue(aggregate.Id, out var loaded)
            || loaded.HasAppended
            || !ReferenceEquals(loaded.Transaction, RequireTransaction())
            || (loaded.Aggregate is not null && !ReferenceEquals(loaded.Aggregate, aggregate))
        )
            throw new InvalidOperationException(
                "Append the aggregate from this store's active observation once."
            );
        if (
            loaded.Aggregate is null
            && (aggregate.ExpectedVersion != 0 || loaded.Expectation is > 0)
        )
            throw new DbUpdateConcurrencyException(
                "Creation requires a loaded missing stream at version zero."
            );
        if (aggregate.PendingEvents.Count == 0)
            throw new InvalidOperationException(
                "Append requires accepted facts; an empty decision needs no append."
            );
        var append = encoder.Encode(loaded.ObservedStream, aggregate, cancellationToken);
        var state = inlineStatePersistence?.Encode(
            loaded.ObservedStream,
            loaded.PersistedStateRow,
            aggregate,
            append.NextVersion,
            append.RecordedAt
        );
        cancellationToken.ThrowIfCancellationRequested();
        // All event/state encoding has succeeded. Only now enter the native unit of work.
        if (loaded.ObservedStream.Version == 0)
        {
            loaded.ObservedStream.CreatedAt = append.RecordedAt;
            database.Set<TStreamRecord>().Add(loaded.ObservedStream);
        }
        else
            database.Set<TStreamRecord>().Attach(loaded.ObservedStream);
        loaded.ObservedStream.Version = append.NextVersion;
        loaded.ObservedStream.UpdatedAt = append.RecordedAt;
        loaded.ObservedStream.ConcurrencyStamp = Guid.NewGuid();
        database.Set<TStoredEventRecord>().AddRange(append.Rows);
        if (state is not null)
            inlineStatePersistence!.AddOrUpdate(loaded.PersistedStateRow, state);

        // Pending facts remain on the aggregate; this observation must not append them twice.
        loaded.HasAppended = true;
        return Task.FromResult(new EventAppendResult(append.NextVersion, append.RecordedAt));
    }

    private IDbContextTransaction RequireTransaction() =>
        database.Database.CurrentTransaction
        ?? throw new InvalidOperationException(
            "The store requires the caller's active native transaction."
        );

    private sealed class StreamWriteObservation(
        TStreamRecord stream,
        TAggregate? aggregate,
        IDbContextTransaction transaction,
        long? expectation,
        object? state
    )
    {
        internal TStreamRecord ObservedStream { get; } = stream;
        internal TAggregate? Aggregate { get; } = aggregate;
        internal IDbContextTransaction Transaction { get; } = transaction;
        internal long? Expectation { get; } = expectation;
        internal object? PersistedStateRow { get; } = state;
        internal bool HasAppended { get; set; }
    }
}
