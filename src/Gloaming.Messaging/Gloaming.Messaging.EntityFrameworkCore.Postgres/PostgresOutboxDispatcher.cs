using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using IsolationLevel = System.Data.IsolationLevel;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>One-message PostgreSQL dispatch. No transaction is held across transport publication.</summary>
/// <typeparam name="TDbContext">The owning module's Npgsql context, independent of producer operations.</typeparam>
public sealed partial class PostgresOutboxDispatcher<TDbContext> : IOutboxDispatcher<TDbContext>
    where TDbContext : DbContext
{
    private readonly TDbContext database;
    private readonly IMessagePublisher publisher;
    private readonly OutboxDispatchOptions options;
    private readonly string table;
    private readonly ILogger<PostgresOutboxDispatcher<TDbContext>>? logger;

    /// <summary>Validates options and native EF model metadata without opening a database connection.</summary>
    /// <remarks>
    /// The context must have its provider/model configured; table existence is checked by actual dispatch SQL.
    /// An optional native logger records selected-attempt failures while the attempt activity remains current.
    /// </remarks>
    public PostgresOutboxDispatcher(
        TDbContext database,
        IMessagePublisher publisher,
        OutboxDispatchOptions options,
        ILogger<PostgresOutboxDispatcher<TDbContext>>? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(options);

        ValidateOptions(options);
        ValidateModel(database);
        this.database = database;
        this.publisher = publisher;
        this.options = options;
        this.logger = logger;
        var entity = database.Model.FindEntityType(typeof(OutboxMessageRecord))!;
        table = database
            .GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    /// <inheritdoc />
    public async Task<OutboxDispatchResult> DispatchNextAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireIndependentOperation();
        var claim = await ClaimAsync(cancellationToken);
        if (claim is null)
            return OutboxDispatchResult.NoWork;

        using var attempt = new MessagingTelemetry.Attempt(
            "dispatch",
            claim.Message.MessageId,
            claim.Message.CorrelationId,
            claim.Message.CausationId,
            claim.Message.TraceParent,
            claim.Message.TraceState
        );
        try
        {
            try
            {
                await publisher.PublishAsync(claim.Message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Acceptance can be ambiguous. Retain the claim for expiry rather than rewriting it.
                throw;
            }
            catch (Exception publicationFailure)
            {
                try
                {
                    await FinishAsync(claim, completed: false, cancellationToken);
                }
                catch (Exception storageFailure)
                {
                    throw new AggregateException(
                        "Publication and retry scheduling both failed.",
                        publicationFailure,
                        storageFailure
                    );
                }

                throw;
            }

            bool completed = await FinishAsync(claim, completed: true, cancellationToken);
            attempt.Complete(completed ? "published" : "claim_lost");
            return completed ? OutboxDispatchResult.Published : OutboxDispatchResult.ClaimLost;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            attempt.Cancel();
            throw;
        }
        catch (Exception failure)
        {
            attempt.Fail(failure);
            if (logger is not null)
                AttemptFailed(logger, "dispatch", claim.Message.MessageId, failure);

            throw;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Message {MessageId} {Operation} attempt failed."
    )]
    private static partial void AttemptFailed(
        ILogger logger,
        string operation,
        Guid messageId,
        Exception failure
    );

    internal static void ValidateOptions(OutboxDispatchOptions options)
    {
        if (options.LeaseDuration <= TimeSpan.Zero || options.RetryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Use a positive lease duration and nonnegative retry delay."
            );
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
        };
        if (
            columns.Any(pair => entity.FindProperty(pair.Key)?.GetColumnName(store) != pair.Value)
            || entity.FindProperty(nameof(OutboxMessageRecord.Payload))!.GetColumnType() != "jsonb"
            || entity.FindProperty(nameof(OutboxMessageRecord.QueuedAt))!.GetDefaultValueSql()
                != "clock_timestamp()"
            || entity.FindProperty(nameof(OutboxMessageRecord.AvailableAt))!.GetDefaultValueSql()
                != "clock_timestamp()"
            || entity.FindPrimaryKey()?.Properties.Count != 1
            || entity.FindPrimaryKey()!.Properties[0].Name != nameof(OutboxMessageRecord.MessageId)
        )
            throw new InvalidOperationException(
                "Retain the supported outbox column/key/JSONB/database-time mapping."
            );
    }

    private void RequireIndependentOperation()
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

    private async Task<Claim?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        await using var command = Command(
            transaction,
            $"""
            WITH candidate AS (
                SELECT message_id FROM {table}
                WHERE dispatched_at IS NULL AND available_at <= clock_timestamp()
                  AND (lease_until IS NULL OR lease_until <= clock_timestamp())
                ORDER BY available_at, queued_at, message_id
                FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE {table} AS outgoing
            SET lease_token = @token, lease_until = clock_timestamp() + @duration,
                attempts = outgoing.attempts + 1
            FROM candidate WHERE outgoing.message_id = candidate.message_id
            RETURNING outgoing.message_id, destination, message_name, schema_version, payload, owner_key,
                      correlation_id, causation_id, trace_parent, trace_state
            """
        );
        Guid token = Guid.NewGuid();
        Parameter(command, "token", token);
        Parameter(command, "duration", options.LeaseDuration);
        Claim? claim = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                using var payload = JsonDocument.Parse(reader.GetString(4));
                claim = new(
                    new OutgoingMessage(
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
                    token
                );
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return claim;
    }

    private async Task<bool> FinishAsync(
        Claim claim,
        bool completed,
        CancellationToken cancellationToken
    )
    {
        RequireIndependentOperation();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        string assignment = completed
            ? "dispatched_at = clock_timestamp()"
            : "available_at = clock_timestamp() + @retry";
        await using var command = Command(
            transaction,
            $"""
            UPDATE {table} SET {assignment}, lease_token = NULL, lease_until = NULL
            WHERE message_id = @id AND lease_token = @token AND dispatched_at IS NULL
              AND lease_until > clock_timestamp()
            """
        );
        Parameter(command, "id", claim.Message.MessageId);
        Parameter(command, "token", claim.Token);
        if (!completed)
            Parameter(command, "retry", options.RetryDelay);
        int changed = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    private DbCommand Command(IDbContextTransaction transaction, string sql)
    {
        var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        command.CommandTimeout = database.Database.GetCommandTimeout() ?? 30;
        return command;
    }

    private static void Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record Claim(OutgoingMessage Message, Guid Token);
}
