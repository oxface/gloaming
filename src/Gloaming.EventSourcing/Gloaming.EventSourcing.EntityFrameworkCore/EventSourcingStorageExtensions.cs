using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Registers explicit native stream/event mappings without choosing consumer ownership, decoding or transactions.</summary>
public static class EventSourcingStorageExtensions
{
    /// <summary>Registers storage using Id/EventId keys and StreamId references, without tenancy.</summary>
    public static ModelBuilder ConfigureEventSourcingStorage<TStream, TEvent>(
        this ModelBuilder model,
        EventSourcingStorageOptions? options = null
    )
        where TStream : class, IEventStreamRecord
        where TEvent : class, IStoredEventRecord =>
        model.ConfigureEventSourcingStorage<TStream, TEvent>(
            row => row.Id,
            row => row.EventId,
            row => row.StreamId,
            options
        );

    /// <summary>
    /// Registers two consumer row types using native key expressions. Payload/provider mapping,
    /// ownership filters, migrations, value population and saving remain consumer responsibilities.
    /// </summary>
    public static ModelBuilder ConfigureEventSourcingStorage<TStream, TEvent>(
        this ModelBuilder model,
        Expression<Func<TStream, object?>> streamKey,
        Expression<Func<TEvent, object?>> eventKey,
        Expression<Func<TEvent, object?>> eventStreamKey,
        EventSourcingStorageOptions? options = null
    )
        where TStream : class, IEventStreamRecord
        where TEvent : class, IStoredEventRecord
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(streamKey);
        ArgumentNullException.ThrowIfNull(eventKey);
        ArgumentNullException.ThrowIfNull(eventStreamKey);
        options ??= new EventSourcingStorageOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.StreamsTable);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.EventsTable);
        if (options.Schema is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(options.Schema);
        if (options.StreamsTable == options.EventsTable || typeof(TStream) == typeof(TEvent))
            throw new ArgumentException(
                "Use distinct stream/event row types and tables.",
                nameof(options)
            );

        var stream = model.Entity<TStream>();
        var stored = model.Entity<TEvent>();
        RequireOrdinaryEntity(stream.Metadata);
        RequireOrdinaryEntity(stored.Metadata);
        stream.ToTable(
            options.StreamsTable,
            options.Schema,
            table => table.HasCheckConstraint("positive_stream_version", "version >= 1")
        );
        stream.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        stream.Property(row => row.StreamType).HasColumnName("stream_type").HasMaxLength(100);
        stream
            .Property(row => row.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        stream
            .Property(row => row.ConcurrencyStamp)
            .HasColumnName("concurrency_stamp")
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        stream.Property(row => row.CreatedAt).HasColumnName("created_at");
        stream.Property(row => row.UpdatedAt).HasColumnName("updated_at");

        stored.ToTable(
            options.EventsTable,
            options.Schema,
            table =>
            {
                table.HasCheckConstraint("positive_event_version", "stream_version >= 1");
                table.HasCheckConstraint("positive_event_schema", "schema_version >= 1");
            }
        );
        stored.Property(row => row.EventId).HasColumnName("event_id").ValueGeneratedNever();
        stored.Property(row => row.StreamId).HasColumnName("stream_id");
        stored.Property(row => row.StreamVersion).HasColumnName("stream_version");
        stored.Property(row => row.EventName).HasColumnName("event_name").HasMaxLength(200);
        stored.Property(row => row.SchemaVersion).HasColumnName("schema_version");
        stored.Property(row => row.RecordedAt).HasColumnName("recorded_at");
        stored.Property(row => row.Payload).HasColumnName("payload");

        var headerKey = stream.HasKey(streamKey).Metadata;
        var envelopeKey = stored.HasKey(eventKey).Metadata;
        RequireIdentity(headerKey.Properties, nameof(IEventStreamRecord.Id));
        RequireIdentity(envelopeKey.Properties, nameof(IStoredEventRecord.EventId));
        var relationship = stored
            .HasOne<TStream>()
            .WithMany()
            .HasForeignKey(eventStreamKey)
            .OnDelete(DeleteBehavior.Restrict)
            .Metadata;
        RequireIdentity(relationship.Properties, nameof(IStoredEventRecord.StreamId));
        if (
            relationship.PrincipalKey != headerKey
            || envelopeKey.Properties.Count != relationship.Properties.Count
            || !envelopeKey
                .Properties.Take(envelopeKey.Properties.Count - 1)
                .SequenceEqual(relationship.Properties.Take(relationship.Properties.Count - 1))
        )
            throw new InvalidOperationException(
                "Stream references and event identities must use matching ownership prefixes and the stream primary key."
            );
        stored
            .HasIndex([
                .. relationship.Properties.Select(property => property.Name),
                nameof(IStoredEventRecord.StreamVersion),
            ])
            .IsUnique();
        return model;
    }

    private static void RequireIdentity(IReadOnlyList<IMutableProperty> properties, string last)
    {
        if (
            properties[^1].Name != last
            || properties.Any(property =>
                property.IsShadowProperty()
                || property.PropertyInfo is null
                || property.GetValueConverter() is not null
                || property.GetProviderClrType() is not null
            )
        )
            throw new InvalidOperationException(
                $"Use mapped, unconverted identity properties ending in {last}."
            );
    }

    private static void RequireOrdinaryEntity(IMutableEntityType entity)
    {
        if (
            entity.BaseType is not null
            || entity.GetDerivedTypes().Any()
            || entity.IsOwned()
            || entity.GetMappingFragments().Any()
            || entity.GetViewName() is not null
        )
            throw new InvalidOperationException("Use ordinary independent single-table row types.");
    }
}
