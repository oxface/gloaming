using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Callable, read-only PostgreSQL observations, independent of dispatch and recovery registration.</summary>
public static class PostgresOutboxDiagnosticsExtensions
{
    /// <summary>Read one exact route's counts and oldest eligible age at a single database statement time.</summary>
    /// <remarks>Requires a fresh independent context. Native grants/RLS apply; EF query filters do not.</remarks>
    public static async Task<OutboxBacklogSnapshot> ReadPostgresOutboxBacklogAsync<TDbContext>(
        this TDbContext database,
        string routeKey,
        CancellationToken cancellationToken = default
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);

        cancellationToken.ThrowIfCancellationRequested();
        var storage = new PostgresOutboxStorage(database);
        storage.RequireFreshContext("Outbox diagnostics");
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        await using var command = storage.Command(
            transaction,
            $"""
            WITH observed AS MATERIALIZED (SELECT statement_timestamp() AS at)
            SELECT observed.at, totals.*
            FROM observed CROSS JOIN LATERAL (
                SELECT
                    count(*) FILTER (WHERE dead_lettered_at IS NULL) AS pending,
                    count(*) FILTER (WHERE dead_lettered_at IS NULL AND available_at <= observed.at
                        AND (lease_until IS NULL OR lease_until <= observed.at)) AS eligible,
                    count(*) FILTER (WHERE dead_lettered_at IS NULL AND available_at > observed.at
                        AND (lease_until IS NULL OR lease_until <= observed.at)) AS delayed,
                    count(*) FILTER (WHERE dead_lettered_at IS NULL AND lease_until > observed.at) AS leased,
                    count(*) FILTER (WHERE dead_lettered_at IS NOT NULL) AS dead_letters,
                    min(queued_at) FILTER (WHERE dead_lettered_at IS NULL AND available_at <= observed.at
                        AND (lease_until IS NULL OR lease_until <= observed.at)) AS oldest_eligible
                FROM {storage.Table}
                WHERE destination = @route AND dispatched_at IS NULL
            ) totals
            """
        );
        PostgresOutboxStorage.Parameter(command, "route", routeKey);
        OutboxBacklogSnapshot snapshot;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            var observed = reader.GetFieldValue<DateTimeOffset>(0);
            TimeSpan? age = reader.IsDBNull(6)
                ? null
                : observed - reader.GetFieldValue<DateTimeOffset>(6);
            snapshot = new(
                observed,
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                age < TimeSpan.Zero ? TimeSpan.Zero : age
            );
        }

        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }
}
