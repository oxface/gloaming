using System.Text.Json;
using Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

#if EARLIER_BUILD
namespace Gloaming.Messaging.DueTimeProof.Earlier;

#else
namespace Gloaming.Messaging.DueTimeProof.Current;

#endif

internal abstract class NativeDatabase(DbContextOptions options) : DbContext(options)
{
    protected static void MapDelivery(ModelBuilder model, string schema)
    {
        NativeDueIntents.Map(model, schema);
        model.ConfigurePostgresOutbox(schema, "outgoing");
        model.ConfigureCommandReceipts(schema);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateCommandReceiptChanges();
        this.ValidateOutboxChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateCommandReceiptChanges();
        this.ValidateOutboxChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

internal sealed class PersonnelDatabase(DbContextOptions<PersonnelDatabase> options)
    : NativeDatabase(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        MapDelivery(model, "personnel_proof");
        var cases = model.Entity<ProbationCase>();
        cases.ToTable("cases", "personnel_proof");
        cases.HasKey(row => new { row.ScopeKey, row.Id });
        cases.Property(row => row.ScopeKey).HasColumnName("scope_key").UseCollation("C");
        cases.Property(row => row.Id).HasColumnName("id");
        var notices = model.Entity<ReminderNotice>();
        notices.ToTable("reminder_notices", "personnel_proof");
        notices.HasKey(row => new
        {
            row.ScopeKey,
            row.CaseId,
            row.OccurrenceId,
        });
        notices.Property(row => row.ScopeKey).UseCollation("C");
    }
}

internal enum CaseStatus
{
    Active = 1,
    Completed = 2,
    Cancelled = 3,
}

internal sealed class ProbationCase
{
    public string ScopeKey { get; set; } = null!;
    public Guid Id { get; set; }
    public Guid OccurrenceId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public string Assignee { get; set; } = null!;
    public CaseStatus Status { get; set; }
}

internal sealed class ReminderNotice
{
    public string ScopeKey { get; set; } = null!;
    public Guid CaseId { get; set; }
    public Guid OccurrenceId { get; set; }
    public string Recipient { get; set; } = null!;
}

internal static class Personnel
{
    internal const string Route = "personnel.reminder";
    private static readonly JsonSerializerOptions CommandJson = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    // Logical reschedule identity belongs to the admitted command, not to the timer occurrence.
    internal static async Task<Replacement> ReplaceOnceAsync(
        PersonnelDatabase database,
        string scope,
        Guid operationId,
        Guid id,
        string assignee,
        DateTimeOffset due,
        CancellationToken token
    )
    {
        if (
            !await database
                .Set<ProbationCase>()
                .AnyAsync(row => row.ScopeKey == scope && row.Id == id, token)
        )
            throw new InvalidOperationException("Admit an existing case before exposing a result.");

        string input = JsonSerializer.Serialize(
            new
            {
                Id = id,
                Assignee = assignee,
                Due = due,
            },
            CommandJson
        );
        var attempt = await new PostgresCommandReceipts<PersonnelDatabase>(database).AcquireAsync(
            new(scope, "replace-reminder", operationId),
            input,
            token
        );
        if (attempt.Result is { } retained)
        {
            if (retained.FormatVersion != 1)
                throw new InvalidDataException("Deploy a compatible reschedule result reader.");

            var replay =
                JsonSerializer.Deserialize<Replacement>(retained.Payload, CommandJson)
                ?? throw new InvalidDataException("A reschedule result is required.");
            if (replay.Occurrence == Guid.Empty || !Enum.IsDefined(replay.Previous))
                throw new InvalidDataException("Invalid retained reschedule result.");

            return replay;
        }

        var result = await ReplaceAsync(database, scope, id, assignee, due, token);
        var response = new Replacement(result.Occurrence, result.Previous);
        attempt.Retain(new(1, JsonSerializer.Serialize(response, CommandJson)));
        return response;
    }

    internal static Guid Open(
        PersonnelDatabase database,
        string scope,
        Guid id,
        string assignee,
        DateTimeOffset due,
        int payloadVersion = 1
    )
    {
        var row = new ProbationCase
        {
            ScopeKey = scope,
            Id = id,
            Assignee = assignee,
            DueAt = due,
            OccurrenceId = Guid.NewGuid(),
            Status = CaseStatus.Active,
        };
        Stage(database, row, payloadVersion);
        database.Add(row);
        return row.OccurrenceId;
    }

    internal static async Task<(Guid Occurrence, Suppression Previous)> ReplaceAsync(
        PersonnelDatabase database,
        string scope,
        Guid id,
        string assignee,
        DateTimeOffset due,
        CancellationToken token
    )
    {
        var row =
            await LockAsync(database, scope, id, token)
            ?? throw new InvalidOperationException("Admit an existing case.");
        if (row.Status != CaseStatus.Active)
            throw new InvalidOperationException(
                "Only an active case may be extended or reassigned."
            );

        var previous = await NativeDueIntents.SuppressAsync(
            database,
            scope,
            id,
            row.OccurrenceId,
            token
        );
        row.OccurrenceId = Guid.NewGuid();
        row.Assignee = assignee;
        row.DueAt = due;
        Stage(database, row, 1);
        return (row.OccurrenceId, previous);
    }

    internal static async Task<Suppression> FinishAsync(
        PersonnelDatabase database,
        string scope,
        Guid id,
        CaseStatus status,
        CancellationToken token
    )
    {
        if (status is not (CaseStatus.Completed or CaseStatus.Cancelled))
            throw new ArgumentOutOfRangeException(nameof(status));

        var row =
            await LockAsync(database, scope, id, token)
            ?? throw new InvalidOperationException("Admit an existing case.");
        var result = await NativeDueIntents.SuppressAsync(
            database,
            scope,
            id,
            row.OccurrenceId,
            token
        );
        row.Status = status;
        row.OccurrenceId = Guid.NewGuid();
        return result;
    }

    internal static async Task<Acceptance> AcceptAsync(
        PersonnelDatabase database,
        string scope,
        OutgoingMessage message,
        DateTimeOffset now,
        CancellationToken token
    )
    {
        NativeDueIntents.RequireInstant(now);
        var payload = Payloads.Read(message, scope, Route);
        var row = await LockAsync(database, scope, payload.SourceId, token);
        if (
            row is null
            || row.Status != CaseStatus.Active
            || row.OccurrenceId != payload.OccurrenceId
        )
            return Acceptance.Stale;

        if (row.DueAt > now)
            return Acceptance.NotDue;

        if (
            await database
                .Set<ReminderNotice>()
                .AnyAsync(
                    value =>
                        value.ScopeKey == scope
                        && value.CaseId == row.Id
                        && value.OccurrenceId == row.OccurrenceId,
                    token
                )
        )
            return Acceptance.Duplicate;

        // This is an accepted notification intent. No SMTP/external effect runs in this transaction.
        database.Add(
            new ReminderNotice
            {
                ScopeKey = scope,
                CaseId = row.Id,
                OccurrenceId = row.OccurrenceId,
                Recipient = row.Assignee,
            }
        );
        return Acceptance.Accepted;
    }

    private static void Stage(PersonnelDatabase database, ProbationCase row, int version) =>
        NativeDueIntents.Stage(
            database,
            row.ScopeKey,
            row.Id,
            row.OccurrenceId,
            row.DueAt,
            Payloads.Create(row.ScopeKey, Route, row.Id, row.OccurrenceId, version)
        );

    private static async Task<ProbationCase?> LockAsync(
        PersonnelDatabase database,
        string scope,
        Guid id,
        CancellationToken token
    )
    {
        NativeDueIntents.RequireTransaction(database);

        var rows = await database
            .Set<ProbationCase>()
            .FromSqlInterpolated(
                $"SELECT * FROM personnel_proof.cases WHERE scope_key = {scope} AND id = {id} FOR UPDATE"
            )
            .ToArrayAsync(token);
        return rows.SingleOrDefault();
    }
}

internal sealed record Replacement(Guid Occurrence, Suppression Previous);
