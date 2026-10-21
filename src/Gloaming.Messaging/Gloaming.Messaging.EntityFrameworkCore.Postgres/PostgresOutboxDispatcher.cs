using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    private readonly PostgresOutboxStorage storage;
    private readonly IOutboxFailurePolicy<TDbContext>? failurePolicy;
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
        ILogger<PostgresOutboxDispatcher<TDbContext>>? logger = null,
        IOutboxFailurePolicy<TDbContext>? failurePolicy = null
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(options);

        ValidateOptions(options);
        storage = new(database);
        this.database = database;
        this.publisher = publisher;
        this.options = options;
        this.logger = logger;
        table = storage.Table;
        this.failurePolicy = failurePolicy;
    }

    /// <inheritdoc />
    public async Task<OutboxDispatchResult> DispatchNextAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        storage.RequireIndependentOperation();
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
                    await RecordFailureAsync(claim, publicationFailure, cancellationToken);
                }
                catch (Exception storageFailure)
                {
                    throw new OutboxFailureAccountingException(
                        claim.Message.MessageId,
                        publicationFailure,
                        storageFailure
                    );
                }

                throw;
            }

            bool completed = await CompleteAsync(claim, cancellationToken);
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

    internal static void ValidateModel(DbContext database) =>
        PostgresOutboxStorage.ValidateModel(database);

    private async Task<Claim?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        await using var command = storage.Command(
            transaction,
            $"""
            WITH candidate AS (
                SELECT message_id FROM {table}
                WHERE dispatched_at IS NULL AND dead_lettered_at IS NULL AND available_at <= clock_timestamp()
                  AND (lease_until IS NULL OR lease_until <= clock_timestamp())
                ORDER BY available_at, queued_at, message_id
                FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE {table} AS outgoing
            SET lease_token = @token, lease_until = clock_timestamp() + @duration,
                attempts = outgoing.attempts + 1, revision = outgoing.revision + 1
            FROM candidate WHERE outgoing.message_id = candidate.message_id
            RETURNING outgoing.message_id, destination, message_name, schema_version, payload, owner_key,
                      correlation_id, causation_id, trace_parent, trace_state
            """
        );
        Guid token = Guid.NewGuid();
        PostgresOutboxStorage.Parameter(command, "token", token);
        PostgresOutboxStorage.Parameter(command, "duration", options.LeaseDuration);
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

    private async Task<bool> CompleteAsync(Claim claim, CancellationToken cancellationToken)
    {
        storage.RequireIndependentOperation();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        // Establish the row lock before the final expiry predicate. A lock wait must not
        // allow a lease that expired while waiting to complete with an earlier clock value.
        await using (
            var locked = storage.Command(
                transaction,
                $"""
                SELECT message_id FROM {table}
                WHERE message_id = @id AND lease_token = @token AND dispatched_at IS NULL
                  AND dead_lettered_at IS NULL AND lease_until > clock_timestamp()
                FOR UPDATE
                """
            )
        )
        {
            PostgresOutboxStorage.Parameter(locked, "id", claim.Message.MessageId);
            PostgresOutboxStorage.Parameter(locked, "token", claim.Token);
            if (await locked.ExecuteScalarAsync(cancellationToken) is null)
                return false;
        }

        await using var command = storage.Command(
            transaction,
            $"""
            UPDATE {table}
            SET dispatched_at = clock_timestamp(), lease_token = NULL, lease_until = NULL,
                revision = revision + 1
            WHERE message_id = @id AND lease_token = @token AND dispatched_at IS NULL
              AND dead_lettered_at IS NULL AND lease_until > clock_timestamp()
            """
        );
        PostgresOutboxStorage.Parameter(command, "id", claim.Message.MessageId);
        PostgresOutboxStorage.Parameter(command, "token", claim.Token);
        int changed = await command.ExecuteNonQueryAsync(cancellationToken);
        // A missing commit response is ambiguous; never classify it as a publication failure.
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    private async Task RecordFailureAsync(
        Claim claim,
        Exception publicationFailure,
        CancellationToken cancellationToken
    )
    {
        storage.RequireIndependentOperation();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        long failureCount;
        long failuresSinceRedrive;
        await using (
            var select = storage.Command(
                transaction,
                $"""
                SELECT failure_count, failures_since_redrive FROM {table}
                WHERE message_id = @id AND lease_token = @token AND dispatched_at IS NULL
                  AND dead_lettered_at IS NULL AND lease_until > clock_timestamp()
                FOR UPDATE
                """
            )
        )
        {
            PostgresOutboxStorage.Parameter(select, "id", claim.Message.MessageId);
            PostgresOutboxStorage.Parameter(select, "token", claim.Token);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return;

            failureCount = checked(reader.GetInt64(0) + 1);
            failuresSinceRedrive = checked(reader.GetInt64(1) + 1);
        }

        var decision = failurePolicy is null
            ? new OutboxFailureDecision.RetryAfter(options.RetryDelay, "publication-failed")
            : failurePolicy.Decide(
                new(claim.Message, publicationFailure, failureCount, failuresSinceRedrive)
            );
        (string code, TimeSpan? delay) = decision switch
        {
            OutboxFailureDecision.RetryAfter retry when retry.Delay >= TimeSpan.Zero => (
                retry.Code,
                retry.Delay
            ),
            OutboxFailureDecision.DeadLetter deadLetter => (deadLetter.Code, (TimeSpan?)null),
            _ => throw new InvalidOperationException(
                "An outbox policy must select nonnegative delayed retry or dead-letter state."
            ),
        };
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128)
            throw new InvalidOperationException(
                "An outbox failure code must be nonblank and at most 128 characters."
            );

        // Locking the row does not renew its lease. Recheck expiry at the final write.
        await using var command = storage.Command(
            transaction,
            $"""
            UPDATE {table}
            SET failure_count = failure_count + 1, failures_since_redrive = failures_since_redrive + 1,
                revision = revision + 1, last_failed_at = clock_timestamp(), last_failure_code = @code,
                dead_lettered_at = CASE WHEN @deadLetter THEN clock_timestamp() ELSE NULL END,
                available_at = CASE WHEN @deadLetter THEN available_at ELSE clock_timestamp() + @delay END,
                lease_token = NULL, lease_until = NULL
            WHERE message_id = @id AND lease_token = @token AND dispatched_at IS NULL
              AND dead_lettered_at IS NULL AND lease_until > clock_timestamp()
            """
        );
        PostgresOutboxStorage.Parameter(command, "id", claim.Message.MessageId);
        PostgresOutboxStorage.Parameter(command, "token", claim.Token);
        PostgresOutboxStorage.Parameter(command, "code", code);
        PostgresOutboxStorage.Parameter(command, "deadLetter", delay is null);
        PostgresOutboxStorage.Parameter(command, "delay", delay ?? TimeSpan.Zero);
        await command.ExecuteNonQueryAsync(cancellationToken);
        // Do not issue compensating accounting after a missing commit response.
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record Claim(OutgoingMessage Message, Guid Token);
}
