using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Native loading and metadata validation for an explicitly mapped inline row.</summary>
public sealed class InlineStateReader<TStreamRecord, TInlineStateRecord>
    where TStreamRecord : class, IEventStreamRecord
    where TInlineStateRecord : class, IInlineStateRecord
{
    private readonly DbContext database;

    // EF relationship metadata maps this state row's foreign-key properties to the stream record's primary key.
    private readonly IForeignKey streamForeignKey;

    /// <summary>Binds native expression-based inline-state lookup to the provided context.</summary>
    public InlineStateReader(DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
        streamForeignKey = InlineStateMetadata.StreamForeignKey(
            database.Model,
            typeof(TStreamRecord),
            typeof(TInlineStateRecord)
        );
    }

    /// <summary>Loads the mapped inline state for the captured stream and validates matching identity/version metadata; it does not repair stale state.</summary>
    public async Task<TInlineStateRecord> ReadAsync(
        TStreamRecord observedStream,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(observedStream);
        if (
            observedStream.Version < 1
            || observedStream.CreatedAt.Offset != TimeSpan.Zero
            || observedStream.UpdatedAt.Offset != TimeSpan.Zero
            || observedStream.CreatedAt > observedStream.UpdatedAt
        )
            throw new InvalidDataException("A valid existing stream record is required.");
        var row = await FindAsync(observedStream, cancellationToken);
        if (row is null)
            throw new InvalidDataException(
                "A required inline state is missing or behind its stream."
            );
        Validate(observedStream, row);
        return row;
    }

    internal async Task<TInlineStateRecord?> FindAsync(
        TStreamRecord observedStream,
        CancellationToken cancellationToken
    )
    {
        var parameter = Expression.Parameter(typeof(TInlineStateRecord), "row");
        Expression predicate = Expression.Constant(true);
        for (int index = 0; index < streamForeignKey.Properties.Count; index++)
        {
            IProperty property = streamForeignKey.Properties[index];
            object? value = streamForeignKey
                .PrincipalKey.Properties[index]
                .PropertyInfo!.GetValue(observedStream);
            predicate = Expression.AndAlso(
                predicate,
                Expression.Equal(
                    Expression.Property(parameter, property.PropertyInfo!),
                    Expression.Constant(value, property.ClrType)
                )
            );
        }
        var row = await database
            .Set<TInlineStateRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                Expression.Lambda<Func<TInlineStateRecord, bool>>(predicate, parameter),
                cancellationToken
            );
        return row;
    }

    /// <summary>Validates an already fetched native row without another database query.</summary>
    public void Validate(TStreamRecord observedStream, TInlineStateRecord row)
    {
        ArgumentNullException.ThrowIfNull(observedStream);
        ArgumentNullException.ThrowIfNull(row);
        if (
            observedStream.Version < 1
            || observedStream.CreatedAt.Offset != TimeSpan.Zero
            || observedStream.UpdatedAt.Offset != TimeSpan.Zero
            || observedStream.CreatedAt > observedStream.UpdatedAt
            || row.Version < observedStream.Version
        )
            throw new InvalidDataException(
                "Inline state is behind an invalid or advanced stream record."
            );
        if (
            !streamForeignKey
                .Properties.Select(property => property.PropertyInfo!.GetValue(row))
                .SequenceEqual(
                    streamForeignKey.PrincipalKey.Properties.Select(property =>
                        property.PropertyInfo!.GetValue(observedStream)
                    )
                )
        )
            throw new InvalidDataException("Inline state must match the complete stream key.");
        if (row.Version > observedStream.Version)
            throw new DbUpdateConcurrencyException(
                "Inline state advanced after reading the stream record."
            );
        if (!row.RecordedAt.EqualsExact(observedStream.UpdatedAt))
            throw new InvalidDataException("Inline state and stream timestamps disagree.");
    }
}
