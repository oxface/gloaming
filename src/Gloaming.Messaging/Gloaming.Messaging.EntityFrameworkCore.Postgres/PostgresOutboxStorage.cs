using System.Data.Common;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

// Outbox-specific native model/command support. Publication never holds this storage's transaction.
internal sealed class PostgresOutboxStorage
{
    private readonly DbContext database;
    internal string Schema { get; }
    internal string TableName { get; }
    internal string Table { get; }

    internal PostgresOutboxStorage(DbContext database)
    {
        ValidateModel(database);
        this.database = database;
        var entity = database.Model.FindEntityType(typeof(OutboxMessageRecord))!;
        Schema = entity.GetSchema()!;
        TableName = entity.GetTableName()!;
        Table = database.GetService<ISqlGenerationHelper>().DelimitIdentifier(TableName, Schema);
    }

    internal static void ValidateModel(DbContext database)
    {
        if (database.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            throw new InvalidOperationException(
                "This outbox implementation requires native Npgsql EF PostgreSQL."
            );

        var entity = database.Model.FindEntityType(typeof(OutboxMessageRecord));
        if (
            entity?.FindAnnotation("Gloaming:OutboxProvider")?.Value is not "Postgres"
            || entity.GetTableName() is not { } name
            || entity.GetSchema() is not { } schema
        )
            throw new InvalidOperationException(
                "ConfigurePostgresOutbox must register the provided record and explicit table/schema."
            );

        var store = StoreObjectIdentifier.Table(name, schema);
        var columns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(OutboxMessageRecord.MessageId)] = "message_id",
            [nameof(OutboxMessageRecord.RouteKey)] = "destination",
            [nameof(OutboxMessageRecord.MessageName)] = "message_name",
            [nameof(OutboxMessageRecord.SchemaVersion)] = "schema_version",
            [nameof(OutboxMessageRecord.Payload)] = "payload",
            [nameof(OutboxMessageRecord.TenantKey)] = "owner_key",
            [nameof(OutboxMessageRecord.CorrelationId)] = "correlation_id",
            [nameof(OutboxMessageRecord.CausationId)] = "causation_id",
            [nameof(OutboxMessageRecord.TraceParent)] = "trace_parent",
            [nameof(OutboxMessageRecord.TraceState)] = "trace_state",
            [nameof(OutboxMessageRecord.QueuedAt)] = "queued_at",
            [nameof(OutboxMessageRecord.AvailableAt)] = "available_at",
            [nameof(OutboxMessageRecord.DispatchedAt)] = "dispatched_at",
            [nameof(OutboxMessageRecord.LeaseToken)] = "lease_token",
            [nameof(OutboxMessageRecord.LeaseUntil)] = "lease_until",
            [nameof(OutboxMessageRecord.Attempts)] = "attempts",
            [nameof(OutboxMessageRecord.Revision)] = "revision",
            [nameof(OutboxMessageRecord.FailureCount)] = "failure_count",
            [nameof(OutboxMessageRecord.FailuresSinceRedrive)] = "failures_since_redrive",
            [nameof(OutboxMessageRecord.LastFailedAt)] = "last_failed_at",
            [nameof(OutboxMessageRecord.LastFailureCode)] = "last_failure_code",
            [nameof(OutboxMessageRecord.DeadLetteredAt)] = "dead_lettered_at",
            [nameof(OutboxMessageRecord.RedriveCount)] = "redrive_count",
            [nameof(OutboxMessageRecord.LastRedrivenAt)] = "last_redriven_at",
        };
        if (
            columns.Any(pair => entity.FindProperty(pair.Key)?.GetColumnName(store) != pair.Value)
            || entity.FindProperty(nameof(OutboxMessageRecord.Payload))!.GetColumnType() != "jsonb"
            || entity.FindProperty(nameof(OutboxMessageRecord.QueuedAt))!.GetDefaultValueSql()
                != "clock_timestamp()"
            || entity.FindProperty(nameof(OutboxMessageRecord.AvailableAt))!.GetDefaultValueSql()
                != "clock_timestamp()"
            || new[]
            {
                nameof(OutboxMessageRecord.Revision),
                nameof(OutboxMessageRecord.FailureCount),
                nameof(OutboxMessageRecord.FailuresSinceRedrive),
                nameof(OutboxMessageRecord.RedriveCount),
            }.Any(name => entity.FindProperty(name)?.GetDefaultValue() is not 0L)
            || entity.FindProperty(nameof(OutboxMessageRecord.LastFailureCode))!.GetMaxLength()
                != 128
            || entity.FindPrimaryKey()?.Properties.Count != 1
            || entity.FindPrimaryKey()!.Properties[0].Name != nameof(OutboxMessageRecord.MessageId)
        )
            throw new InvalidOperationException(
                "Retain the supported outbox column/key/JSONB/database-time mapping."
            );
    }

    internal void RequireIndependentOperation()
    {
        if (
            database.Database.CurrentTransaction is not null
            || Transaction.Current is not null
            || database.Database.GetEnlistedTransaction() is not null
            || database.ChangeTracker.HasChanges()
        )
            throw new InvalidOperationException(
                "Dispatch requires an independent context without a producer, enlisted or ambient transaction or pending business changes."
            );
    }

    internal void RequireFreshContext(string operation = "Outbox dead-letter operations")
    {
        RequireIndependentOperation();
        if (database.ChangeTracker.Entries().Any())
            throw new InvalidOperationException(
                $"{operation} require a fresh context without tracked entities."
            );
    }

    internal DbCommand Command(IDbContextTransaction transaction, string sql)
    {
        var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        command.CommandTimeout = database.Database.GetCommandTimeout() ?? 30;
        return command;
    }

    internal static void Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
