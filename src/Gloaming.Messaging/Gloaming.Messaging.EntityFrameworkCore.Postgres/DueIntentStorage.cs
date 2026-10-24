using System.Data;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

internal sealed class DueIntentStorage
{
    internal string Table { get; }

    internal DueIntentStorage(DbContext database)
    {
        PostgresOutboxStorage.ValidateModel(database);
        var entity = database.Model.FindEntityType(typeof(DueIntentRecord));
        if (
            entity?.FindAnnotation(PostgresDueIntentConfiguration.Capability)?.Value is not true
            || entity.GetTableName() is not { } table
            || entity.GetSchema() is not { } schema
        )
            throw new InvalidOperationException(
                "ConfigurePostgresDueIntents must map the provided occurrence record."
            );

        PostgresDueIntentConfiguration.ValidateIdentifier(schema);
        PostgresDueIntentConfiguration.ValidateIdentifier(table);
        var store = StoreObjectIdentifier.Table(table, schema);
        Dictionary<string, string> columns = new(StringComparer.Ordinal)
        {
            [nameof(DueIntentRecord.ScopeKey)] = "scope_key",
            [nameof(DueIntentRecord.ScheduleId)] = "schedule_id",
            [nameof(DueIntentRecord.OccurrenceId)] = "occurrence_id",
            [nameof(DueIntentRecord.DueAt)] = "due_at",
            [nameof(DueIntentRecord.State)] = "state",
            [nameof(DueIntentRecord.MessageId)] = "message_id",
            [nameof(DueIntentRecord.RouteKey)] = "destination",
            [nameof(DueIntentRecord.MessageName)] = "message_name",
            [nameof(DueIntentRecord.SchemaVersion)] = "schema_version",
            [nameof(DueIntentRecord.Payload)] = "payload",
            [nameof(DueIntentRecord.TenantKey)] = "tenant_key",
            [nameof(DueIntentRecord.CorrelationId)] = "correlation_id",
            [nameof(DueIntentRecord.CausationId)] = "causation_id",
            [nameof(DueIntentRecord.TraceParent)] = "trace_parent",
            [nameof(DueIntentRecord.TraceState)] = "trace_state",
        };
        string[] key =
        [
            nameof(DueIntentRecord.ScopeKey),
            nameof(DueIntentRecord.ScheduleId),
            nameof(DueIntentRecord.OccurrenceId),
        ];
        if (
            entity.BaseType is not null
            || entity.IsOwned()
            || entity.GetDerivedTypes().Any()
            || entity.GetMappingFragments().Any()
            || entity.GetViewName() is not null
            || entity.GetProperties().Count() != columns.Count
            || columns.Any(pair =>
                entity.FindProperty(pair.Key)?.GetColumnName(store) != pair.Value
            )
            || entity
                .GetProperties()
                .Any(property =>
                    property.IsShadowProperty()
                    || property.GetValueConverter() is not null
                    || property.GetProviderClrType() is not null
                    || property.ValueGenerated != ValueGenerated.Never
                    || property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save
                    || property.GetAfterSaveBehavior() == PropertySaveBehavior.Ignore
                )
            || entity
                .FindPrimaryKey()
                ?.Properties.Select(property => property.Name)
                .SequenceEqual(key)
                is not true
            || entity.FindProperty(nameof(DueIntentRecord.Payload))?.GetColumnType() != "jsonb"
            || entity.FindProperty(nameof(DueIntentRecord.DueAt))?.GetColumnType()
                is not ("timestamp with time zone" or "timestamp(6) with time zone")
            || entity.FindProperty(nameof(DueIntentRecord.DueAt))?.GetPrecision() != 6
            || entity.FindProperty(nameof(DueIntentRecord.ScopeKey))?.GetMaxLength() != 256
            || database
                .GetService<IDesignTimeModel>()
                .Model.FindEntityType(typeof(DueIntentRecord))!
                .FindProperty(nameof(DueIntentRecord.ScopeKey))
                ?.GetCollation() != "C"
            || entity.FindProperty(nameof(DueIntentRecord.State))?.IsConcurrencyToken is not true
            || entity.FindProperty(nameof(DueIntentRecord.State))?.GetColumnType() != "integer"
            || !entity
                .GetIndexes()
                .Any(index =>
                    index.IsUnique
                    && index.GetFilter() is null
                    && index.Properties.Count == 1
                    && index.Properties[0].Name == nameof(DueIntentRecord.MessageId)
                )
        )
            throw new InvalidOperationException(
                "Retain the fixed occurrence key, columns, ordinal scope, JSONB, native state concurrency and delivery identity mapping."
            );

        var outbox = database.Model.FindEntityType(typeof(OutboxMessageRecord))!;
        if (outbox.GetTableName() == table && outbox.GetSchema() == schema)
            throw new InvalidOperationException(
                "Occurrences and outbox envelopes require independent tables."
            );

        Table = database.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, schema);
    }

    internal static IDbContextTransaction RequireTransaction(DbContext database)
    {
        if (
            database.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().Connection is null
            || transaction.GetDbTransaction().IsolationLevel
                != System.Data.IsolationLevel.ReadCommitted
            || System.Transactions.Transaction.Current is not null
            || database.Database.GetEnlistedTransaction() is not null
        )
            throw new InvalidOperationException(
                "Use the original active native PostgreSQL ReadCommitted transaction; dispose failed operation state."
            );

        return transaction;
    }

    internal static void RequireInstant(DateTimeOffset instant)
    {
        if (instant.Offset != TimeSpan.Zero || instant.Ticks % 10 != 0)
            throw new ArgumentException(
                "Use an explicit UTC instant at PostgreSQL microsecond precision."
            );
    }

    internal static void ValidateStored(DueIntentRecord row)
    {
        try
        {
            _ = new DueIntentKey(row.ScopeKey, row.ScheduleId, row.OccurrenceId);
            RequireInstant(row.DueAt);
            _ = row.Message();
            if (!Enum.IsDefined(row.State))
                throw new ArgumentOutOfRangeException(nameof(row));
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException(
                "The retained occurrence key, time, state or envelope is invalid.",
                error
            );
        }
    }
}
