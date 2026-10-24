global using DueIntent = Gloaming.Messaging.EntityFrameworkCore.Postgres.DueIntentRecord;
global using IntentState = Gloaming.Messaging.EntityFrameworkCore.Postgres.DueIntentState;
global using Suppression = Gloaming.Messaging.EntityFrameworkCore.Postgres.DueIntentSuppression;
using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

[assembly: InternalsVisibleTo("OutboxPostgresTests")]


#if EARLIER_BUILD
namespace Gloaming.Messaging.DueTimeProof.Earlier;

#else
namespace Gloaming.Messaging.DueTimeProof.Current;

#endif

internal enum Acceptance
{
    Accepted = 1,
    Duplicate = 2,
    Stale = 3,
    NotDue = 4,
}

// Test composition helpers delegate all occurrence mechanics to the public library.
internal static class NativeDueIntents
{
    internal static void Map(ModelBuilder model, string schema) =>
        model.ConfigurePostgresDueIntents(schema);

    internal static void Stage(
        DbContext database,
        string scope,
        Guid schedule,
        Guid occurrence,
        DateTimeOffset due,
        OutgoingMessage message
    ) =>
        new PostgresDueIntents<DbContext>(database).Stage(
            new(scope, schedule, occurrence),
            due,
            message
        );

    internal static Task<Suppression> SuppressAsync(
        DbContext database,
        string scope,
        Guid schedule,
        Guid occurrence,
        CancellationToken token
    ) =>
        new PostgresDueIntents<DbContext>(database).SuppressAsync(
            new(scope, schedule, occurrence),
            token
        );

    internal static async Task<Guid[]> ReleaseAsync<T>(
        T database,
        string scope,
        DateTimeOffset cutoff,
        int maximum,
        CancellationToken token
    )
        where T : DbContext =>
        (
            await new PostgresDueIntents<T>(database).ReleaseAsync(scope, cutoff, maximum, token)
        ).ToArray();

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
