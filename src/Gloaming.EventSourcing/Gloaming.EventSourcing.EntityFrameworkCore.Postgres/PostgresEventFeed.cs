using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlTypes;
using IsolationLevel = System.Data.IsolationLevel;

namespace Gloaming.EventSourcing.EntityFrameworkCore.Postgres;

/// <summary>
/// Explicit bounded read/projection/cutover operations over the installed v1 native feed.
/// Each operation owns a fresh consumer-established scope; no worker or retry loop is installed.
/// </summary>
public sealed class PostgresEventFeed<TContext>
    where TContext : DbContext
{
    private readonly IServiceScopeFactory scopes;
    private readonly EventFeedLocation location;

    /// <summary>Uses consumer-established fresh scopes and an explicitly installed feed.</summary>
    public PostgresEventFeed(IServiceScopeFactory scopes, EventFeedLocation location)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(location);

        this.scopes = scopes;
        this.location = location;
    }

    /// <summary>Reads one statement snapshot without tracking, writes or a consumer callback.</summary>
    public async Task<FeedPage> ReadAsync(
        FeedCursor after,
        FeedLimits limits,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(limits);

        RejectAmbient();
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TContext>();
        RequireFresh(database);
        await database.Database.OpenConnectionAsync(cancellationToken);
        return await FeedStorage.ReadAsync(database, location, after, limits, cancellationToken);
    }

    /// <summary>
    /// Applies one page and checkpoint in one transaction. The cooperative callback stages only
    /// generation-qualified native projection writes; it must not save, commit, append source facts
    /// or perform external effects. Failed scopes are discarded and no retry is automatic.
    /// </summary>
    public Task<ProjectionResult> ProjectNextAsync(
        ProjectionGeneration<TContext> generation,
        FeedLimits limits,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(limits);

        return ExecuteAsync(
            async (database, transaction, token) =>
            {
                if (!await TryLockAsync(database, generation.Name, generation.Generation, token))
                    return new(ProjectionStatus.Busy);

                await using (
                    var insert = FeedStorage.Command(
                        database,
                        $"""
                        INSERT INTO {location.Table(
                            "projection_checkpoints"
                        )}(name, generation, definition, cursor)
                        VALUES(@name, @generation, @definition, @cursor) ON CONFLICT(name, generation) DO NOTHING
                        """
                    )
                )
                {
                    insert.Parameters.AddWithValue("name", generation.Name);
                    insert.Parameters.AddWithValue("generation", generation.Generation);
                    insert.Parameters.AddWithValue("definition", generation.DefinitionId);
                    insert.Parameters.AddWithValue(
                        "cursor",
                        FeedCursor.Start(location.FeedId).Value
                    );
                    await insert.ExecuteNonQueryAsync(token);
                }

                var (definition, after) = await CheckpointAsync(
                    database,
                    generation.Name,
                    generation.Generation,
                    token
                );
                if (!StringComparer.Ordinal.Equals(definition, generation.DefinitionId))
                    throw new InvalidOperationException(
                        "This generation retains another reducer definition. Use a new generation."
                    );

                var page = await FeedStorage.ReadAsync(database, location, after, limits, token);
                if (page.Events.Count > 0)
                {
                    database.SavingChanges += RejectCallbackSave;
                    try
                    {
                        await generation.Apply(database, generation.Generation, page, token);
                    }
                    finally
                    {
                        database.SavingChanges -= RejectCallbackSave;
                    }

                    RequireTransaction(database, transaction);
                    var writes = new ProjectionWrites(database);
                    await database.SaveChangesAsync(token);
                    RequireTransaction(database, transaction);
                    await writes.RequireStoredAsync(database, token);
                    RequireTransaction(database, transaction);
                }

                await using var update = FeedStorage.Command(
                    database,
                    $"""
                    UPDATE {location.Table("projection_checkpoints")} SET cursor = @next
                    WHERE name = @name AND generation = @generation AND definition = @definition AND cursor = @after
                    """
                );
                update.Parameters.AddWithValue("next", page.Next.Value);
                update.Parameters.AddWithValue("name", generation.Name);
                update.Parameters.AddWithValue("generation", generation.Generation);
                update.Parameters.AddWithValue("definition", generation.DefinitionId);
                update.Parameters.AddWithValue("after", after.Value);
                if (await update.ExecuteNonQueryAsync(token) != 1)
                    throw new InvalidOperationException(
                        "The original checkpoint was changed outside this turn."
                    );

                return new(
                    page.Events.Count > 0 ? ProjectionStatus.Applied : ProjectionStatus.NoWork,
                    page
                );
            },
            cancellationToken
        );
    }

    /// <summary>Fences appends briefly and changes the active pointer only when the candidate is caught up.</summary>
    public Task<ProjectionResult> ActivateAsync(
        string name,
        string generation,
        string? expectedActiveGeneration,
        CancellationToken cancellationToken = default
    )
    {
        ProjectionGeneration<TContext>.Identity(name);
        ProjectionGeneration<TContext>.Identity(generation);
        if (expectedActiveGeneration is not null)
            ProjectionGeneration<TContext>.Identity(expectedActiveGeneration);

        return ExecuteAsync(
            async (database, _, token) =>
            {
                if (!await TryLockAsync(database, name, generation, token))
                    return new(ProjectionStatus.Busy);

                var (_, cursor) = await CheckpointAsync(database, name, generation, token);
                await using (
                    var head = FeedStorage.Command(
                        database,
                        $"SELECT protocol, feed_id, position FROM {location.Table("feed_head")} WHERE singleton FOR UPDATE"
                    )
                )
                await using (var reader = await head.ExecuteReaderAsync(token))
                {
                    if (
                        !await reader.ReadAsync(token)
                        || reader.GetInt32(0) != 1
                        || reader.GetGuid(1) != location.FeedId
                        || cursor.Data.Position > reader.GetInt64(2)
                    )
                        throw new InvalidOperationException(
                            "The installed feed identity or protocol differs."
                        );
                }

                string? active;
                await using (
                    var read = FeedStorage.Command(
                        database,
                        $"SELECT generation FROM {location.Table("projection_active")} WHERE name = @name FOR UPDATE"
                    )
                )
                {
                    read.Parameters.AddWithValue("name", name);
                    active = await read.ExecuteScalarAsync(token) as string;
                }

                if (StringComparer.Ordinal.Equals(active, generation))
                    return new(ProjectionStatus.AlreadyActive);

                if (!StringComparer.Ordinal.Equals(active, expectedActiveGeneration))
                    return new(ProjectionStatus.Conflict);

                // A bounded metadata query avoids transferring an oversized poison envelope at activation.
                await using (
                    var remaining = FeedStorage.Command(
                        database,
                        $"""
                        SELECT EXISTS(SELECT 1 FROM {location.Table("feed_entries")} e
                            WHERE e.position > @position OR (e.position = @position AND @source IS NOT NULL AND
                                (e.source, e.stream_sort, e.version, e.event_sort) > (@source COLLATE "C", @stream COLLATE "C", @version, @event COLLATE "C")))
                        """
                    )
                )
                {
                    remaining.Parameters.AddWithValue("position", cursor.Data.Position);
                    remaining.Parameters.AddWithValue(
                        "source",
                        NpgsqlDbType.Text,
                        (object?)cursor.Data.Source ?? DBNull.Value
                    );
                    remaining.Parameters.AddWithValue(
                        "stream",
                        NpgsqlDbType.Text,
                        (object?)cursor.Data.Stream ?? DBNull.Value
                    );
                    remaining.Parameters.AddWithValue(
                        "event",
                        NpgsqlDbType.Text,
                        (object?)cursor.Data.Event ?? DBNull.Value
                    );
                    remaining.Parameters.AddWithValue("version", cursor.Data.Version);
                    if ((bool)(await remaining.ExecuteScalarAsync(token))!)
                        return new(ProjectionStatus.NotCaughtUp);
                }

                await using var write = FeedStorage.Command(
                    database,
                    $"""
                    INSERT INTO {location.Table(
                        "projection_active"
                    )}(name, generation) VALUES(@name, @generation)
                    ON CONFLICT(name) DO UPDATE SET generation = EXCLUDED.generation
                    """
                );
                write.Parameters.AddWithValue("name", name);
                write.Parameters.AddWithValue("generation", generation);
                await write.ExecuteNonQueryAsync(token);
                return new(ProjectionStatus.Activated);
            },
            cancellationToken
        );
    }

    private async Task<(string Definition, FeedCursor Cursor)> CheckpointAsync(
        TContext database,
        string name,
        string generation,
        CancellationToken token
    )
    {
        await using var command = FeedStorage.Command(
            database,
            $"SELECT definition, cursor FROM {location.Table("projection_checkpoints")} WHERE name = @name AND generation = @generation FOR UPDATE"
        );
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("generation", generation);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("No retained candidate checkpoint exists.");

        var cursor = FeedCursor.Parse(reader.GetString(1));
        if (cursor.Data.FeedId != location.FeedId)
            throw new InvalidOperationException("The checkpoint belongs to another feed.");

        return (reader.GetString(0), cursor);
    }

    private async Task<bool> TryLockAsync(
        TContext database,
        string name,
        string generation,
        CancellationToken token
    )
    {
        byte[] key = SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(
                new[] { "gloaming.projection.v1", location.FeedId.ToString("D"), name, generation }
            )
        );
        await using var command = FeedStorage.Command(
            database,
            "SELECT pg_catalog.pg_try_advisory_xact_lock(@key)"
        );
        command.Parameters.AddWithValue("key", BinaryPrimitives.ReadInt64BigEndian(key));
        return (bool)(await command.ExecuteScalarAsync(token))!;
    }

    private async Task<ProjectionResult> ExecuteAsync(
        Func<TContext, IDbContextTransaction, CancellationToken, Task<ProjectionResult>> action,
        CancellationToken token
    )
    {
        AsyncServiceScope? scope = null;
        IDbContextTransaction? transaction = null;
        ProjectionResult result;
        bool committing = false;
        try
        {
            RejectAmbient();
            token.ThrowIfCancellationRequested();
            scope = scopes.CreateAsyncScope();
            var database = scope.Value.ServiceProvider.GetRequiredService<TContext>();
            RequireFresh(database);
            transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                token
            );
            result = await action(database, transaction, token);
            RequireTransaction(database, transaction);
            if (
                result.Status
                is ProjectionStatus.Applied
                    or ProjectionStatus.NoWork
                    or ProjectionStatus.Activated
            )
            {
                token.ThrowIfCancellationRequested();
                committing = true;
                await transaction.CommitAsync(token);
            }
        }
        catch (Exception error)
        {
            result = new(
                committing ? ProjectionStatus.Indeterminate
                    : error is OperationCanceledException && token.IsCancellationRequested
                        ? ProjectionStatus.Cancelled
                    : ProjectionStatus.Failed,
                Error: error
            );
        }

        // Each resource gets its disposal attempt; cleanup never erases an acknowledged commit.
        Exception? cleanup = await DisposeAsync(transaction, null);
        if (scope is { } created)
            cleanup = await DisposeAsync(created, cleanup);

        if (cleanup is not null)
            result = result with
            {
                Error = result.Error is null
                    ? cleanup
                    : new AggregateException(result.Error, cleanup),
            };

        return result;
    }

    private static async Task<Exception?> DisposeAsync(IAsyncDisposable? resource, Exception? prior)
    {
        try
        {
            if (resource is not null)
                await resource.DisposeAsync();

            return prior;
        }
        catch (Exception error)
        {
            return prior is null ? error : new AggregateException(prior, error);
        }
    }

    private static void RejectAmbient()
    {
        if (Transaction.Current is not null)
            throw new InvalidOperationException(
                "Feed operations own native transactions; ambient transactions are unsupported."
            );
    }

    private static void RequireFresh(TContext database)
    {
        var connection = FeedStorage.Connection(database);
        if (
            connection.State != System.Data.ConnectionState.Closed
            || database.Database.CurrentTransaction is not null
            || database.Database.GetEnlistedTransaction() is not null
            || database.ChangeTracker.Entries().Any()
        )
            throw new InvalidOperationException("Feed operations require a fresh native context.");
    }

    private static void RequireTransaction(TContext database, IDbContextTransaction transaction)
    {
        if (
            !ReferenceEquals(database.Database.CurrentTransaction, transaction)
            || transaction.GetDbTransaction().Connection != FeedStorage.Connection(database)
        )
            throw new InvalidOperationException(
                "The callback replaced or completed the owning transaction."
            );
    }

    private static void RejectCallbackSave(object? sender, SavingChangesEventArgs args) =>
        throw new InvalidOperationException(
            "Projection callbacks stage native changes; the selected feed operation saves them."
        );
}
