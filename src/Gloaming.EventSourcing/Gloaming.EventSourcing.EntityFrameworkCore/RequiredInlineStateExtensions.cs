using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Explicit model declaration and native save-boundary validation; no saving or transaction wrapper.</summary>
public static class RequiredInlineStateExtensions
{
    private const string Prefix = "ModulithFoundry:RequiredInlineState:";

    public static ModelBuilder ConfigureRequiredInlineState<TStream, TStoredEvent, TState>(
        this ModelBuilder model,
        string streamType
    )
        where TStream : class, IEventStreamRecord
        where TStoredEvent : class, IStoredEventRecord
        where TState : class, IInlineStateRecord
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamType);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(streamType.Length, 100);
        var stream =
            model.Model.FindEntityType(typeof(TStream))
            ?? throw new InvalidOperationException("Map the stream first.");
        var events =
            model.Model.FindEntityType(typeof(TStoredEvent))
            ?? throw new InvalidOperationException("Map the event envelope first.");
        var state =
            model.Model.FindEntityType(typeof(TState))
            ?? throw new InvalidOperationException("Map the inline row first.");
        _ = InlineStateMetadata.StreamForeignKey(model.Model, typeof(TStream), typeof(TState));
        if (
            events
                .GetForeignKeys()
                .SingleOrDefault(key => key.PrincipalKey == stream.FindPrimaryKey())
            is null
        )
            throw new InvalidOperationException(
                "Events must reference the complete stream primary key."
            );
        if (
            stream.FindProperty(nameof(IEventStreamRecord.Version))
                is not { IsConcurrencyToken: true, ValueGenerated: ValueGenerated.Never }
            || stream.FindProperty(nameof(IEventStreamRecord.ConcurrencyStamp))
                is not { IsConcurrencyToken: true, ValueGenerated: ValueGenerated.Never }
        )
            throw new InvalidOperationException("Use the native stream version concurrency token.");
        if (
            stream
                .GetAnnotations()
                .Any(item =>
                    item.Name.StartsWith(Prefix, StringComparison.Ordinal)
                    && item.Value is string[] registered
                    && registered[2] == state.Name
                )
        )
            throw new InvalidOperationException(
                "Use a distinct inline row type per registered aggregate family."
            );
        string annotation = AnnotationName(streamType);
        if (stream.FindAnnotation(annotation) is not null)
            throw new InvalidOperationException(
                "This required inline state is already registered."
            );
        stream.SetAnnotation(annotation, new[] { streamType, events.Name, state.Name });
        model.RequireGloamingCapability("event-streams");
        return model;
    }

    internal static bool IsRegistered(
        IModel model,
        Type stream,
        Type events,
        Type state,
        string family
    ) =>
        model.FindEntityType(stream)?.FindAnnotation(AnnotationName(family))?.Value
            is string[] registration
        && registration.SequenceEqual(
            new[] { family, model.FindEntityType(events)?.Name, model.FindEntityType(state)?.Name }
        );

    internal static bool HasRegistration(IModel model, Type stream, string family) =>
        model.FindEntityType(stream)?.FindAnnotation(AnnotationName(family)) is not null;

    internal static bool IsStateRegistered(IModel model, Type stream, Type state, string family) =>
        model.FindEntityType(stream)?.FindAnnotation(AnnotationName(family))?.Value
            is string[] types
        && types[2] == model.FindEntityType(state)?.Name;

    private static string AnnotationName(string family) => Prefix + "main:" + family;

    /// <summary>Call from both native save override paths. Validates tracked writes before issuing SQL.</summary>
    public static void ValidateEventStreamChanges(this DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);
        database.ChangeTracker.DetectChanges();
        EntityEntry[] entries = database.ChangeTracker.Entries().ToArray();
        if (ValidateMaintenanceWrite(database, entries))
            return;
        foreach (var streamModel in database.Model.GetEntityTypes())
        {
            foreach (
                var annotation in streamModel
                    .GetAnnotations()
                    .Where(item => item.Name.StartsWith(Prefix, StringComparison.Ordinal))
            )
            {
                string[] types = (string[])annotation.Value!;
                string family = types[0];
                var eventModel = database.Model.FindEntityType(types[1])!;
                var stateModel = database.Model.FindEntityType(types[2])!;
                var streamKey = streamModel.FindPrimaryKey()!;
                var eventReference = eventModel
                    .GetForeignKeys()
                    .Single(key => key.PrincipalKey == streamKey);
                var stateReference = stateModel
                    .GetForeignKeys()
                    .Single(key => key.PrincipalKey == streamKey);
                var headers = entries.Where(entry => entry.Metadata == streamModel).ToArray();
                var events = entries
                    .Where(entry =>
                        entry.Metadata == eventModel && entry.State != EntityState.Unchanged
                    )
                    .ToArray();
                var states = entries
                    .Where(entry =>
                        entry.Metadata == stateModel && entry.State != EntityState.Unchanged
                    )
                    .ToArray();
                ValidateInsertedEvents(headers, events, streamKey, eventReference);
                foreach (var header in headers)
                    ValidateAdvancingStream(
                        database,
                        header,
                        events,
                        states,
                        streamKey,
                        eventReference,
                        stateReference,
                        family
                    );
                ValidateStateWrites(headers, states, streamKey, stateReference, family);
            }
        }
    }

    private static void ValidateInsertedEvents(
        EntityEntry[] headers,
        EntityEntry[] events,
        IKey streamKey,
        IForeignKey eventReference
    )
    {
        foreach (var fact in events)
        {
            if (fact.State != EntityState.Added)
                Fail("Configured event envelopes are append-only.");
            var key = InlineStateMetadata.Values(fact, eventReference.Properties);
            var header = headers.SingleOrDefault(entry =>
                key.SequenceEqual(InlineStateMetadata.Values(entry, streamKey.Properties))
            );
            if (header is null || header.State is not (EntityState.Added or EntityState.Modified))
                Fail("An inserted event requires its tracked advancing stream header.");
        }
    }

    private static void ValidateAdvancingStream(
        DbContext database,
        EntityEntry header,
        EntityEntry[] events,
        EntityEntry[] states,
        IKey streamKey,
        IForeignKey eventReference,
        IForeignKey stateReference,
        string family
    )
    {
        var stream = (IEventStreamRecord)header.Entity;
        string? originalFamily =
            header.State == EntityState.Added
                ? null
                : (string?)header.Property(nameof(IEventStreamRecord.StreamType)).OriginalValue;
        if (stream.StreamType != family && originalFamily != family)
            return;
        if (header.State == EntityState.Unchanged)
            return;
        if (
            stream.StreamType != family
            || header.State is not (EntityState.Added or EntityState.Modified)
        )
            Fail("Registered aggregate streams cannot change family or be deleted.");
        if (database.Database.CurrentTransaction is null)
            Fail("Registered aggregate writes require an explicit native transaction.");
        if (
            stream.ConcurrencyStamp == Guid.Empty
            || (
                header.State == EntityState.Modified
                && (
                    !header.Property(nameof(IEventStreamRecord.ConcurrencyStamp)).IsModified
                    || Equals(
                        header.Property(nameof(IEventStreamRecord.ConcurrencyStamp)).OriginalValue,
                        stream.ConcurrencyStamp
                    )
                )
            )
        )
            Fail("Every append must advance the native header concurrency stamp.");
        long previous =
            header.State == EntityState.Added
                ? 0
                : (long)header.Property(nameof(IEventStreamRecord.Version)).OriginalValue!;
        if (
            previous < 0
            || (header.State == EntityState.Modified && previous == 0)
            || stream.Version <= previous
            || stream.CreatedAt.Offset != TimeSpan.Zero
            || stream.UpdatedAt.Offset != TimeSpan.Zero
            || stream.CreatedAt > stream.UpdatedAt
        )
            Fail("The registered stream must advance with valid UTC metadata.");
        if (
            header.State == EntityState.Modified
            && (
                !header.Property(nameof(IEventStreamRecord.Version)).IsModified
                || !InlineStateMetadata
                    .Values(header, streamKey.Properties, true)
                    .SequenceEqual(InlineStateMetadata.Values(header, streamKey.Properties))
                || !stream.CreatedAt.EqualsExact(
                    (DateTimeOffset)
                        header.Property(nameof(IEventStreamRecord.CreatedAt)).OriginalValue!
                )
            )
        )
            Fail("Preserve the stream key, creation time and original version token.");
        object?[] key = InlineStateMetadata.Values(header, streamKey.Properties);
        ValidateRequiredState(header, stream, previous, key, states, stateReference);
        ValidateEventRange(header, stream, previous, key, events, eventReference);
    }

    private static void ValidateRequiredState(
        EntityEntry header,
        IEventStreamRecord stream,
        long previous,
        object?[] key,
        EntityEntry[] states,
        IForeignKey stateReference
    )
    {
        var state = states.SingleOrDefault(entry =>
            key.SequenceEqual(InlineStateMetadata.Values(entry, stateReference.Properties))
        );
        if (state is null || state.State != header.State)
            Fail("Every registered append requires matching changed aggregate state.");
        var row = (IInlineStateRecord)state!.Entity;
        if (row.Version != stream.Version || !row.RecordedAt.EqualsExact(stream.UpdatedAt))
            Fail("Aggregate state must represent the advanced stream version and recorded time.");
        if (
            state.State == EntityState.Modified
            && (
                (long)state.Property(nameof(IInlineStateRecord.Version)).OriginalValue! != previous
                || !state.Property(nameof(IInlineStateRecord.Version)).IsModified
                || !InlineStateMetadata
                    .Values(state, stateReference.Properties, true)
                    .SequenceEqual(key)
            )
        )
            Fail("Preserve the observed inline key/version token.");
    }

    private static void ValidateEventRange(
        EntityEntry header,
        IEventStreamRecord stream,
        long previous,
        object?[] key,
        EntityEntry[] events,
        IForeignKey eventReference
    )
    {
        var batch = events
            .Where(entry =>
                key.SequenceEqual(InlineStateMetadata.Values(entry, eventReference.Properties))
            )
            .Select(entry => (IStoredEventRecord)entry.Entity)
            .OrderBy(row => row.StreamVersion)
            .ToArray();
        if (batch.LongLength != stream.Version - previous)
            Fail("The entire advanced event range must be included in this save.");
        DateTimeOffset time =
            previous == 0
                ? stream.CreatedAt
                : (DateTimeOffset)
                    header.Property(nameof(IEventStreamRecord.UpdatedAt)).OriginalValue!;
        for (int index = 0; index < batch.Length; index++)
        {
            var fact = batch[index];
            if (
                fact.StreamVersion != previous + index + 1
                || fact.RecordedAt.Offset != TimeSpan.Zero
                || fact.RecordedAt < time
                || fact.RecordedAt > stream.UpdatedAt
            )
                Fail("New event positions and UTC times must match the captured range.");
            time = fact.RecordedAt;
        }
        if (
            batch.Length == 0
            || !time.EqualsExact(stream.UpdatedAt)
            || (previous == 0 && !batch[0].RecordedAt.EqualsExact(stream.CreatedAt))
        )
            Fail("Event range endpoints must match stream timestamps.");
    }

    private static void ValidateStateWrites(
        EntityEntry[] headers,
        EntityEntry[] states,
        IKey streamKey,
        IForeignKey stateReference,
        string family
    )
    {
        foreach (var state in states)
        {
            var key = InlineStateMetadata.Values(state, stateReference.Properties);
            var header = headers.SingleOrDefault(entry =>
                key.SequenceEqual(InlineStateMetadata.Values(entry, streamKey.Properties))
            );
            if (
                header is null
                || header.State is not (EntityState.Added or EntityState.Modified)
                || ((IEventStreamRecord)header.Entity).StreamType != family
            )
                Fail("Aggregate state cannot change independently of its registered append.");
        }
    }

    [DoesNotReturn]
    private static void Fail(string message) => throw new InvalidOperationException(message);

    // Only library replay may replace state without appending facts. Remember its exact native write
    // for the caller's save guard; scheduling, retries and completion remain outside this record.
    private static readonly ConditionalWeakTable<DbContext, MaintenanceWrite> MaintenanceWrites =
        new();

    internal static bool HasMaintenanceWrite(DbContext database) =>
        MaintenanceWrites.TryGetValue(database, out _);

    internal static void RequireMaintenanceWrite(
        DbContext database,
        Guid id,
        IDbContextTransaction transaction
    )
    {
        if (
            !MaintenanceWrites.TryGetValue(database, out var repair)
            || !ReferenceEquals(repair.Transaction, transaction)
            || repair.Header.Entity is not IEventStreamRecord header
            || header.Id != id
        )
            Fail(
                "The provided rebuilder must stage this target in the selected native context/transaction."
            );

        database.ChangeTracker.DetectChanges();
        ValidateMaintenanceWrite(database, database.ChangeTracker.Entries().ToArray());
    }

    internal static bool IsMaintenanceConflict(
        DbContext database,
        IDbContextTransaction transaction,
        DbUpdateConcurrencyException failure
    ) =>
        MaintenanceWrites.TryGetValue(database, out var repair)
        && ReferenceEquals(repair.Transaction, transaction)
        && failure.Entries.Count > 0
        && failure.Entries.All(entry =>
            ReferenceEquals(entry.Context, database)
            && (
                ReferenceEquals(entry.Entity, repair.Header.Entity)
                || ReferenceEquals(entry.Entity, repair.State.Entity)
            )
        );

    internal static void RecordMaintenanceWrite(
        DbContext database,
        IEventStreamRecord header,
        IInlineStateRecord state,
        IDbContextTransaction transaction
    )
    {
        database.ChangeTracker.DetectChanges();
        MaintenanceWrites.Add(
            database,
            new(database.Entry(header), database.Entry(state), transaction)
        );
    }

    private static bool ValidateMaintenanceWrite(DbContext database, EntityEntry[] entries)
    {
        if (!MaintenanceWrites.TryGetValue(database, out var repair))
            return false;
        if (
            !ReferenceEquals(repair.Transaction, database.Database.CurrentTransaction)
            || entries.Length != 2
            || !entries.Any(entry => ReferenceEquals(entry.Entity, repair.Header.Entity))
            || !entries.Any(entry => ReferenceEquals(entry.Entity, repair.State.Entity))
        )
            Fail("Save the exact maintenance header/state in its original transaction.");
        foreach (var entry in entries)
        {
            var expected = ReferenceEquals(entry.Entity, repair.Header.Entity)
                ? repair.HeaderValues
                : repair.StateValues;
            if (
                entry.State
                    is not (EntityState.Added or EntityState.Modified or EntityState.Unchanged)
                || !expected.Properties.All(property =>
                    expected[property] is DateTimeOffset recordedTime
                        ? entry.Property(property.Name).CurrentValue is DateTimeOffset currentTime
                            && recordedTime.EqualsExact(currentTime)
                        : Equals(expected[property], entry.Property(property.Name).CurrentValue)
                )
            )
                Fail("The maintenance replacement changed after reconstruction.");
        }
        var header = repair.Header;
        var state = repair.State;
        if (
            header.State == EntityState.Modified
            && (
                header.Properties.Any(property =>
                    property.IsModified
                    && property.Metadata.Name != nameof(IEventStreamRecord.ConcurrencyStamp)
                )
                || !Equals(
                    header.Property(nameof(IEventStreamRecord.ConcurrencyStamp)).OriginalValue,
                    repair.OriginalStamp
                )
            )
        )
            Fail(
                "Repair changes only the concurrency stamp, preserving its observed original token."
            );
        if (
            state.State == EntityState.Modified
            && (
                !Equals(
                    state.Property(nameof(IInlineStateRecord.Version)).OriginalValue,
                    repair.OriginalStateVersion
                )
                || !InlineStateMetadata
                    .Values(state, state.Metadata.FindPrimaryKey()!.Properties, true)
                    .SequenceEqual(
                        InlineStateMetadata.Values(
                            state,
                            state.Metadata.FindPrimaryKey()!.Properties
                        )
                    )
            )
        )
            Fail("Preserve the observed maintenance state key/version.");
        return true;
    }

    private sealed class MaintenanceWrite(
        EntityEntry header,
        EntityEntry state,
        IDbContextTransaction transaction
    )
    {
        internal EntityEntry Header { get; } = header;
        internal EntityEntry State { get; } = state;
        internal PropertyValues HeaderValues { get; } = header.CurrentValues.Clone();
        internal PropertyValues StateValues { get; } = state.CurrentValues.Clone();
        internal object? OriginalStamp { get; } =
            header.Property(nameof(IEventStreamRecord.ConcurrencyStamp)).OriginalValue;
        internal object? OriginalStateVersion { get; } =
            state.Property(nameof(IInlineStateRecord.Version)).OriginalValue;
        internal IDbContextTransaction Transaction { get; } = transaction;
    }
}
