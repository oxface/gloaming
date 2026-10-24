using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Gloaming.EventSourcing.EntityFrameworkCore.Postgres;

internal static class FeedStorage
{
    internal static NpgsqlConnection Connection(DbContext database)
    {
        if (
            database.Database.GetDbConnection() is not NpgsqlConnection connection
            || new NpgsqlConnectionStringBuilder(connection.ConnectionString).Multiplexing
        )
            throw new InvalidOperationException(
                "The feed requires a dedicated native Npgsql connection without multiplexing."
            );

        return connection;
    }

    internal static NpgsqlCommand Command(DbContext database, string sql)
    {
        var command = new NpgsqlCommand(
            sql,
            Connection(database),
            database.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction
        );
        command.CommandTimeout = database.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    internal static async Task<FeedPage> ReadAsync(
        DbContext database,
        EventFeedLocation location,
        FeedCursor after,
        FeedLimits limits,
        CancellationToken token
    )
    {
        if (after.Data.FeedId != location.FeedId)
            throw new ArgumentException("The cursor belongs to another feed.", nameof(after));

        string entries = location.Table("feed_entries");
        await using var command = Command(
            database,
            $"""
            WITH head AS MATERIALIZED (SELECT position, protocol, feed_id FROM {location.Table(
                "feed_head"
            )} WHERE singleton),
            candidates AS MATERIALIZED (
                SELECT e.*, pg_catalog.octet_length(e.envelope::text)::bigint bytes
                FROM {entries} e CROSS JOIN head h
                WHERE e.position <= h.position AND
                    (e.position > @position OR (e.position = @position AND @source IS NOT NULL AND
                        (e.source, e.stream_sort, e.version, e.event_sort) > (@source COLLATE "C", @stream COLLATE "C", @version, @event COLLATE "C")))
                ORDER BY e.position, e.source, e.stream_sort, e.version, e.event_sort LIMIT @count
            ), bounded AS (
                SELECT *, sum(bytes) OVER (ORDER BY position, source, stream_sort, version, event_sort) total,
                    row_number() OVER (ORDER BY position, source, stream_sort, version, event_sort) ordinal FROM candidates
            )
            SELECT h.position, h.protocol, h.feed_id, b.position, b.source, b.stream_key::text, b.version, b.event_key::text,
                CASE WHEN b.total <= @bytes AND b.ordinal <= @max THEN b.envelope::text END, b.bytes
            FROM head h LEFT JOIN bounded b ON true
            ORDER BY b.position, b.source, b.stream_sort, b.version, b.event_sort
            """
        );
        command.Parameters.AddWithValue("position", after.Data.Position);
        command.Parameters.AddWithValue(
            "source",
            NpgsqlDbType.Text,
            (object?)after.Data.Source ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "stream",
            NpgsqlDbType.Text,
            (object?)after.Data.Stream ?? DBNull.Value
        );
        command.Parameters.AddWithValue("version", after.Data.Version);
        command.Parameters.AddWithValue(
            "event",
            NpgsqlDbType.Text,
            (object?)after.Data.Event ?? DBNull.Value
        );
        command.Parameters.AddWithValue("count", limits.MaxEvents + 1);
        command.Parameters.AddWithValue("max", limits.MaxEvents);
        command.Parameters.AddWithValue("bytes", limits.MaxPayloadBytes);
        await using var reader = await command.ExecuteReaderAsync(token);
        var events = new List<FeedEvent>();
        long? head = null;
        bool more = false;
        while (await reader.ReadAsync(token))
        {
            head = reader.GetInt64(0);
            if (
                reader.GetInt32(1) != 1
                || reader.GetGuid(2) != location.FeedId
                || after.Data.Position > head
            )
                throw new InvalidOperationException(
                    "The retained feed identity, protocol or cursor head differs."
                );

            if (reader.IsDBNull(3))
                continue;

            if (reader.IsDBNull(8))
            {
                if (events.Count == 0)
                    throw new FeedEventTooLargeException(
                        reader.GetInt64(3),
                        reader.GetString(4),
                        reader.GetString(7),
                        reader.GetInt64(9)
                    );

                more = true;
                continue;
            }

            events.Add(
                new(
                    reader.GetInt64(3),
                    reader.GetString(4),
                    Json(reader.GetString(5)),
                    reader.GetInt64(6),
                    Json(reader.GetString(7)),
                    Json(reader.GetString(8))
                )
            );
        }

        if (head is null)
            throw new InvalidOperationException("Missing retained feed head.");

        FeedCursor next = more
            ? FeedCursor.After(location.FeedId, events[^1])
            : FeedCursor.Fence(location.FeedId, head.Value);
        return new(events.AsReadOnly(), next, head.Value, more);
    }

    private static JsonElement Json(string value)
    {
        using var parsed = JsonDocument.Parse(value);
        return parsed.RootElement.Clone();
    }
}
