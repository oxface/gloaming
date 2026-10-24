using System.Transactions;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;

internal sealed class RecurrenceStorage
{
    internal string Table { get; }

    internal RecurrenceStorage(DbContext database)
    {
        _ = new PostgresDueIntents<DbContext>(database);
        var entity = database.Model.FindEntityType(typeof(RecurringIntentRecord));
        if (
            entity?.FindAnnotation(PostgresRecurrenceConfiguration.Capability)?.Value is not true
            || entity.GetTableName() is not { } table
            || entity.GetSchema() is not { } schema
        )
            throw new InvalidOperationException(
                "ConfigurePostgresRecurrence must map the provided generation record."
            );

        PostgresRecurrenceConfiguration.Identifier(schema);
        PostgresRecurrenceConfiguration.Identifier(table);
        var store = StoreObjectIdentifier.Table(table, schema);
        string[] names =
        [
            nameof(RecurringIntentRecord.ScopeKey),
            nameof(RecurringIntentRecord.ScheduleId),
            nameof(RecurringIntentRecord.GenerationId),
            nameof(RecurringIntentRecord.Definition),
            nameof(RecurringIntentRecord.Progress),
            nameof(RecurringIntentRecord.Revision),
            nameof(RecurringIntentRecord.State),
        ];
        string[] columns =
        [
            "scope_key",
            "schedule_id",
            "generation_id",
            "definition",
            "progress",
            "revision",
            "state",
        ];
        if (
            entity.BaseType is not null
            || entity.GetDeclaredQueryFilters().Count != 0
            || entity.IsOwned()
            || entity.GetDerivedTypes().Any()
            || entity.GetMappingFragments().Any()
            || entity.GetViewName() is not null
            || entity.GetProperties().Count() != names.Length
            || names
                .Where((name, i) => entity.FindProperty(name)?.GetColumnName(store) != columns[i])
                .Any()
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
                .SequenceEqual(names.Take(3))
                is not true
            || entity.FindProperty(nameof(RecurringIntentRecord.Revision))?.IsConcurrencyToken
                is not true
            || entity.FindProperty(nameof(RecurringIntentRecord.Revision))?.GetColumnType()
                != "bigint"
            || entity.FindProperty(nameof(RecurringIntentRecord.State))?.GetColumnType()
                != "integer"
            || entity.FindProperty(nameof(RecurringIntentRecord.Definition))?.GetColumnType()
                != "text"
            || entity.FindProperty(nameof(RecurringIntentRecord.Progress))?.GetColumnType()
                != "text"
            || entity.FindProperty(nameof(RecurringIntentRecord.ScopeKey))?.GetMaxLength() != 256
            || database
                .GetService<IDesignTimeModel>()
                .Model.FindEntityType(typeof(RecurringIntentRecord))!
                .FindProperty(nameof(RecurringIntentRecord.ScopeKey))
                ?.GetCollation() != "C"
        )
            throw new InvalidOperationException(
                "Retain the fixed recurrence identity, ordinal scope, exact descriptor/progress and native revision concurrency model."
            );

        if (
            database
                .Model.GetEntityTypes()
                .Any(other =>
                    other.ClrType != typeof(RecurringIntentRecord)
                    && other.GetTableName() == table
                    && other.GetSchema() == schema
                )
        )
            throw new InvalidOperationException(
                "Recurrence generations require an independent native table."
            );

        if (
            database.Model.FindEntityType(typeof(DueIntentRecord))!.GetDeclaredQueryFilters().Count
                != 0
            || database
                .Model.FindEntityType(
                    typeof(Gloaming.Messaging.EntityFrameworkCore.OutboxMessageRecord)
                )!
                .GetDeclaredQueryFilters()
                .Count != 0
        )
            throw new InvalidOperationException(
                "Recurrence requires unfiltered native finite-intent and outbox records."
            );

        Table = database.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, schema);
    }

    internal static IDbContextTransaction Transaction(DbContext database)
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
                "Use the original active native PostgreSQL ReadCommitted transaction."
            );

        return transaction;
    }
}
