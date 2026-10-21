using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

// Private shared native mapping/SQL support, not a provider dialect or public claim protocol.
internal sealed class PostgresInboxStorage
{
    private readonly DbContext database;
    internal string Table { get; }

    internal PostgresInboxStorage(DbContext database)
    {
        if (database.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            throw new InvalidOperationException(
                "This inbox implementation requires native Npgsql EF PostgreSQL."
            );

        var entity = database.Model.FindEntityType(typeof(InboxMessageRecord));
        if (
            entity?.FindAnnotation("Gloaming:InboxProvider")?.Value is not "Postgres"
            || entity.GetTableName() is not { } table
            || entity.GetSchema() is not { } schema
        )
            throw new InvalidOperationException(
                "ConfigurePostgresInbox must register the provided record and explicit schema/table."
            );

        var store = StoreObjectIdentifier.Table(table, schema);
        var columns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(InboxMessageRecord.SubscriptionKey)] = "subscription_key",
            [nameof(InboxMessageRecord.ProducerKey)] = "producer_key",
            [nameof(InboxMessageRecord.MessageId)] = "message_id",
            [nameof(InboxMessageRecord.MessageName)] = "message_name",
            [nameof(InboxMessageRecord.SchemaVersion)] = "schema_version",
            [nameof(InboxMessageRecord.Payload)] = "payload",
            [nameof(InboxMessageRecord.TenantKey)] = "tenant_key",
            [nameof(InboxMessageRecord.CorrelationId)] = "correlation_id",
            [nameof(InboxMessageRecord.CausationId)] = "causation_id",
            [nameof(InboxMessageRecord.TraceParent)] = "trace_parent",
            [nameof(InboxMessageRecord.TraceState)] = "trace_state",
            [nameof(InboxMessageRecord.ReceivedAt)] = "received_at",
            [nameof(InboxMessageRecord.AvailableAt)] = "available_at",
            [nameof(InboxMessageRecord.ProcessedAt)] = "processed_at",
            [nameof(InboxMessageRecord.Revision)] = "revision",
            [nameof(InboxMessageRecord.FailureCount)] = "failure_count",
            [nameof(InboxMessageRecord.FailuresSinceRedrive)] = "failures_since_redrive",
            [nameof(InboxMessageRecord.LastFailedAt)] = "last_failed_at",
            [nameof(InboxMessageRecord.LastFailureCode)] = "last_failure_code",
            [nameof(InboxMessageRecord.QuarantinedAt)] = "quarantined_at",
            [nameof(InboxMessageRecord.RedriveCount)] = "redrive_count",
            [nameof(InboxMessageRecord.LastRedrivenAt)] = "last_redriven_at",
        };
        string[] key =
        [
            nameof(InboxMessageRecord.SubscriptionKey),
            nameof(InboxMessageRecord.ProducerKey),
            nameof(InboxMessageRecord.MessageId),
        ];
        if (
            columns.Any(pair => entity.FindProperty(pair.Key)?.GetColumnName(store) != pair.Value)
            || entity.FindProperty(nameof(InboxMessageRecord.Payload))!.GetColumnType() != "jsonb"
            || entity.FindProperty(nameof(InboxMessageRecord.ReceivedAt))!.GetDefaultValueSql()
                != "clock_timestamp()"
            || entity.FindProperty(nameof(InboxMessageRecord.AvailableAt))!.GetDefaultValueSql()
                != "clock_timestamp()"
            || new[]
            {
                nameof(InboxMessageRecord.Revision),
                nameof(InboxMessageRecord.FailureCount),
                nameof(InboxMessageRecord.FailuresSinceRedrive),
                nameof(InboxMessageRecord.RedriveCount),
            }.Any(name => entity.FindProperty(name)?.GetDefaultValue() is not 0L)
            || entity.FindProperty(nameof(InboxMessageRecord.LastFailureCode))!.GetMaxLength()
                != 128
            || entity.FindPrimaryKey()?.Properties.Select(item => item.Name).SequenceEqual(key)
                is not true
        )
            throw new InvalidOperationException(
                "Retain the supported inbox key/columns/JSONB/database-time mapping."
            );

        this.database = database;
        Table = database.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, schema);
    }

    internal void RequireFreshContext()
    {
        if (
            database.Database.CurrentTransaction is not null
            || Transaction.Current is not null
            || database.Database.GetEnlistedTransaction() is not null
            || database.ChangeTracker.Entries().Any()
        )
            throw new InvalidOperationException(
                "Inbox processing/recovery requires a fresh context without tracked entities or native/ambient/enlisted transactions."
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

    internal sealed record LockedDelivery(
        IncomingMessage Message,
        long FailureCount,
        long FailuresSinceRedrive
    );

    internal async Task<LockedDelivery?> LockNextAsync(
        IDbContextTransaction transaction,
        string subscriptionKey,
        CancellationToken cancellationToken
    )
    {
        await using var command = Command(
            transaction,
            $"""
            SELECT message_id, producer_key, message_name, schema_version, payload, tenant_key, correlation_id, causation_id, trace_parent, trace_state, failure_count, failures_since_redrive
            FROM {Table}
            WHERE subscription_key = @subscription AND processed_at IS NULL AND quarantined_at IS NULL AND available_at <= clock_timestamp()
            ORDER BY available_at, received_at, producer_key, message_id
            FOR UPDATE SKIP LOCKED LIMIT 1
            """
        );
        Parameter(command, "subscription", subscriptionKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        using var payload = JsonDocument.Parse(reader.GetString(4));
        return new LockedDelivery(
            new IncomingMessage(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                payload.RootElement,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)
            ),
            reader.GetInt64(10),
            reader.GetInt64(11)
        );
    }

    internal async Task CompleteAsync(
        IDbContextTransaction transaction,
        string subscriptionKey,
        IncomingMessage message,
        CancellationToken cancellationToken
    )
    {
        await using var command = Command(
            transaction,
            $"""
            UPDATE {Table} SET processed_at = clock_timestamp(), revision = revision + 1
            WHERE subscription_key = @subscription AND producer_key = @producer AND message_id = @id AND processed_at IS NULL AND quarantined_at IS NULL
            """
        );
        IdentityParameters(command, subscriptionKey, message);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DbUpdateConcurrencyException(
                "The locked inbox delivery disappeared or completed outside its supported lifecycle."
            );
    }

    internal async Task RecordFailureAsync(
        IDbContextTransaction transaction,
        string subscriptionKey,
        IncomingMessage message,
        InboxFailureDecision decision,
        CancellationToken cancellationToken
    )
    {
        (string code, TimeSpan? delay) = decision switch
        {
            InboxFailureDecision.RetryAfter retry when retry.Delay > TimeSpan.Zero => (
                retry.Code,
                retry.Delay
            ),
            InboxFailureDecision.Quarantine quarantine => (quarantine.Code, (TimeSpan?)null),
            _ => throw new InvalidOperationException(
                "An inbox failure policy must select positive delayed retry or quarantine."
            ),
        };
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128)
            throw new InvalidOperationException(
                "An inbox failure code must be nonblank and at most 128 characters."
            );

        await using var command = Command(
            transaction,
            $"""
            UPDATE {Table}
            SET failure_count = failure_count + 1, failures_since_redrive = failures_since_redrive + 1,
                revision = revision + 1, last_failed_at = clock_timestamp(), last_failure_code = @code,
                quarantined_at = CASE WHEN @quarantine THEN clock_timestamp() ELSE NULL END,
                available_at = CASE WHEN @quarantine THEN available_at ELSE clock_timestamp() + @delay END
            WHERE subscription_key = @subscription AND producer_key = @producer AND message_id = @id
                AND processed_at IS NULL AND quarantined_at IS NULL
            """
        );
        IdentityParameters(command, subscriptionKey, message);
        Parameter(command, "code", code);
        Parameter(command, "quarantine", delay is null);
        Parameter(command, "delay", delay ?? TimeSpan.Zero);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DbUpdateConcurrencyException(
                "The locked inbox delivery left its supported lifecycle before failure accounting."
            );
    }

    internal static void EnvelopeParameters(
        DbCommand command,
        string subscriptionKey,
        IncomingMessage message
    )
    {
        IdentityParameters(command, subscriptionKey, message);
        Parameter(command, "name", message.MessageName);
        Parameter(command, "schema", message.SchemaVersion);
        Parameter(command, "payload", message.Payload.GetRawText());
        Parameter(command, "tenant", message.TenantKey);
        Parameter(command, "correlation", message.CorrelationId);
        Parameter(command, "causation", message.CausationId);
        Parameter(command, "traceParent", message.TraceParent);
        Parameter(command, "traceState", message.TraceState);
    }

    private static void IdentityParameters(
        DbCommand command,
        string subscriptionKey,
        IncomingMessage message
    )
    {
        Parameter(command, "subscription", subscriptionKey);
        Parameter(command, "producer", message.ProducerKey);
        Parameter(command, "id", message.MessageId);
    }

    internal static void Parameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        if (value is null)
            parameter.DbType = DbType.String;
        command.Parameters.Add(parameter);
    }
}
