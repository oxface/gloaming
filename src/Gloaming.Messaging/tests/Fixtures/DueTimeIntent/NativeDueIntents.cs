using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

[assembly: InternalsVisibleTo("OutboxPostgresTests")]


#if EARLIER_BUILD
namespace Gloaming.Messaging.DueTimeProof.Earlier;

#else
namespace Gloaming.Messaging.DueTimeProof.Current;

#endif

// Test-owned native experiment. No public Gloaming scheduling interface is selected.
internal enum IntentState
{
    Pending = 1,
    Cancelled = 2,
    Released = 3,
}

internal enum Suppression
{
    Suppressed = 1,
    AlreadyCancelled = 2,
    AlreadyReleased = 3,
    Missing = 4,
}

internal enum Acceptance
{
    Accepted = 1,
    Duplicate = 2,
    Stale = 3,
    NotDue = 4,
}

internal sealed class DueIntent
{
    public string ScopeKey { get; set; } = null!;
    public Guid ScheduleId { get; set; }
    public Guid OccurrenceId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public IntentState State { get; set; }
    public Guid MessageId { get; set; }
    public string Route { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int SchemaVersion { get; set; }
    public string Payload { get; set; } = null!;
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
}

internal static class NativeDueIntents
{
    internal static void Map(ModelBuilder model, string schema)
    {
        var row = model.Entity<DueIntent>();
        row.ToTable("due_intents", schema);
        row.HasKey(value => new
        {
            value.ScopeKey,
            value.ScheduleId,
            value.OccurrenceId,
        });
        row.Property(value => value.ScopeKey).HasColumnName("scope_key").UseCollation("C");
        row.Property(value => value.ScheduleId).HasColumnName("schedule_id");
        row.Property(value => value.OccurrenceId).HasColumnName("occurrence_id");
        row.Property(value => value.DueAt).HasColumnName("due_at");
        row.Property(value => value.State).HasColumnName("state").HasConversion<int>();
        row.HasIndex(value => new
        {
            value.ScopeKey,
            value.State,
            value.DueAt,
        });
    }

    internal static void Stage(
        DbContext database,
        string scope,
        Guid schedule,
        Guid occurrence,
        DateTimeOffset due,
        OutgoingMessage message
    )
    {
        RequireTransaction(database);
        RequireInstant(due);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentOutOfRangeException.ThrowIfEqual(schedule, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(occurrence, Guid.Empty);
        if (!string.Equals(scope, message.TenantKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The admitted scope must match the retained envelope."
            );

        database.Add(
            new DueIntent
            {
                ScopeKey = scope,
                ScheduleId = schedule,
                OccurrenceId = occurrence,
                DueAt = due,
                State = IntentState.Pending,
                MessageId = message.MessageId,
                Route = message.RouteKey,
                Name = message.MessageName,
                SchemaVersion = message.SchemaVersion,
                Payload = message.Payload.GetRawText(),
                CorrelationId = message.CorrelationId,
                CausationId = message.CausationId,
                TraceParent = message.TraceParent,
                TraceState = message.TraceState,
            }
        );
    }

    internal static async Task<Suppression> SuppressAsync(
        DbContext database,
        string scope,
        Guid schedule,
        Guid occurrence,
        CancellationToken token
    )
    {
        RequireTransaction(database);

        string sql =
            $"SELECT * FROM {Table<DueIntent>(database)} WHERE scope_key = {{0}} AND schedule_id = {{1}} AND occurrence_id = {{2}} FOR UPDATE";
        var rows = await database
            .Set<DueIntent>()
            .FromSql(FormattableStringFactory.Create(sql, scope, schedule, occurrence))
            .ToArrayAsync(token);
        if (rows.Length == 0)
            return Suppression.Missing;

        var row = rows[0];
        if (row.State == IntentState.Released)
            return Suppression.AlreadyReleased;

        if (row.State == IntentState.Cancelled)
            return Suppression.AlreadyCancelled;

        if (row.State != IntentState.Pending)
            throw new InvalidDataException("Unsupported native intent state.");

        row.State = IntentState.Cancelled;
        return Suppression.Suppressed;
    }

    internal static async Task<Guid[]> ReleaseAsync<T>(
        T database,
        string scope,
        DateTimeOffset cutoff,
        int maximum,
        CancellationToken token
    )
        where T : DbContext
    {
        RequireTransaction(database);
        RequireInstant(cutoff);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 100);

        string sql =
            $"SELECT * FROM {Table<DueIntent>(database)} WHERE scope_key = {{0}} AND state = 1 AND due_at <= {{1}} ORDER BY due_at, schedule_id, occurrence_id LIMIT {{2}} FOR UPDATE SKIP LOCKED";
        var rows = await database
            .Set<DueIntent>()
            .FromSql(FormattableStringFactory.Create(sql, scope, cutoff, maximum))
            .ToArrayAsync(token);
        var outbox = new EfOutbox<T>(database);
        foreach (var row in rows)
        {
            using var payload = JsonDocument.Parse(row.Payload);
            outbox.Enqueue(
                new(
                    row.MessageId,
                    row.Route,
                    row.Name,
                    row.SchemaVersion,
                    payload.RootElement,
                    tenantKey: row.ScopeKey,
                    correlationId: row.CorrelationId,
                    causationId: row.CausationId,
                    traceParent: row.TraceParent,
                    traceState: row.TraceState
                )
            );
            row.State = IntentState.Released;
        }

        return rows.Select(row => row.MessageId).ToArray();
    }

    internal static string Table<T>(DbContext database)
    {
        var entity = database.Model.FindEntityType(typeof(T))!;
        return database
            .GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    internal static void RequireTransaction(DbContext database)
    {
        if (
            !database.Database.IsNpgsql()
            || database.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().Connection is null
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted
            || System.Transactions.Transaction.Current is not null
        )
            throw new InvalidOperationException("Use a fresh native ReadCommitted transaction.");
    }

    internal static void RequireInstant(DateTimeOffset instant)
    {
        // PostgreSQL stores microseconds. Reject finer precision rather than silently advancing eligibility.
        if (instant.Offset != TimeSpan.Zero || instant.Ticks % 10 != 0)
            throw new ArgumentException(
                "Use an explicit UTC instant at PostgreSQL microsecond precision."
            );
    }
}

internal sealed record DuePayload(Guid SourceId, Guid OccurrenceId);

internal sealed record DuePayloadV2(Guid SourceId, Guid OccurrenceId, string Presentation);

internal static class Payloads
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    internal static OutgoingMessage Create(
        string scope,
        string route,
        Guid source,
        Guid occurrence,
        int version
    ) =>
        version switch
        {
            1 => OutgoingMessage.FromPayload(
                Guid.NewGuid(),
                route,
                route,
                1,
                new DuePayload(source, occurrence),
                Json,
                scope
            ),
            2 => OutgoingMessage.FromPayload(
                Guid.NewGuid(),
                route,
                route,
                2,
                new DuePayloadV2(source, occurrence, "compact"),
                Json,
                scope
            ),
            _ => throw new InvalidDataException("Unsupported producer payload."),
        };

    internal static DuePayload Read(OutgoingMessage message, string scope, string route)
    {
        if (
            !string.Equals(message.TenantKey, scope, StringComparison.Ordinal)
            || message.MessageName != route
            || message.RouteKey != route
        )
            throw new InvalidDataException("Admit the expected scope and wire contract first.");

        DuePayload result;
#if EARLIER_BUILD
        if (message.SchemaVersion != 1)
            throw new InvalidDataException("Deploy a compatible payload reader.");

        result = message.Payload.Deserialize<DuePayload>(Json)!;
#else
        result = message.SchemaVersion switch
        {
            1 => message.Payload.Deserialize<DuePayload>(Json)!,
            2 => ReadV2(message),
            _ => throw new InvalidDataException("Deploy a compatible payload reader."),
        };
#endif
        if (result is null || result.SourceId == Guid.Empty || result.OccurrenceId == Guid.Empty)
            throw new InvalidDataException(
                "A due occurrence requires its original source/generation."
            );

        return result;
    }

#if !EARLIER_BUILD
    private static DuePayload ReadV2(OutgoingMessage message)
    {
        var value =
            message.Payload.Deserialize<DuePayloadV2>(Json)
            ?? throw new InvalidDataException("A due occurrence requires a payload.");
        return new(value.SourceId, value.OccurrenceId);
    }
#endif
}
