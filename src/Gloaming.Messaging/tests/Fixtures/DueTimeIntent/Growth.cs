using Microsoft.EntityFrameworkCore;

#if EARLIER_BUILD
namespace Gloaming.Messaging.DueTimeProof.Earlier;

#else
namespace Gloaming.Messaging.DueTimeProof.Current;

#endif

internal sealed class GrowthDatabase(DbContextOptions<GrowthDatabase> options)
    : NativeDatabase(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        MapDelivery(model, "growth_proof");
        var cycles = model.Entity<ReviewCycle>();
        cycles.ToTable("cycles", "growth_proof");
        cycles.HasKey(row => new { row.ScopeKey, row.PersonId });
        cycles.Property(row => row.ScopeKey).HasColumnName("scope_key").UseCollation("C");
        cycles.Property(row => row.PersonId).HasColumnName("person_id");
        var indications = model.Entity<PreparationIndication>();
        indications.ToTable("preparation_indications", "growth_proof");
        indications.HasKey(row => new
        {
            row.ScopeKey,
            row.PersonId,
            row.OccurrenceId,
        });
        indications.Property(row => row.ScopeKey).UseCollation("C");
    }
}

internal sealed class ReviewCycle
{
    public string ScopeKey { get; set; } = null!;
    public Guid PersonId { get; set; }
    public DateTimeOffset ApprovedClosureAt { get; set; }
    public DateTimeOffset PreparationAt { get; set; }
    public Guid OccurrenceId { get; set; }
    public bool UnfinishedReviewCancelled { get; set; }
}

internal sealed class PreparationIndication
{
    public string ScopeKey { get; set; } = null!;
    public Guid PersonId { get; set; }
    public Guid OccurrenceId { get; set; }
}

internal static class Growth
{
    internal const string Route = "growth.prepare-review";

    // The caller supplies its calculated date. No annual/cron/calendar policy is inferred here.
    internal static async Task<Guid> ApprovedClosureAsync(
        GrowthDatabase database,
        string scope,
        Guid person,
        DateTimeOffset closure,
        DateTimeOffset preparation,
        CancellationToken token
    )
    {
        NativeDueIntents.RequireInstant(closure);
        NativeDueIntents.RequireInstant(preparation);

        var row = await LockAsync(database, scope, person, token);
        if (row is null)
        {
            row = new ReviewCycle { ScopeKey = scope, PersonId = person };
            database.Add(row);
        }
        else
            await NativeDueIntents.SuppressAsync(database, scope, person, row.OccurrenceId, token);

        row.ApprovedClosureAt = closure;
        row.PreparationAt = preparation;
        row.OccurrenceId = Guid.NewGuid();
        row.UnfinishedReviewCancelled = false;
        NativeDueIntents.Stage(
            database,
            scope,
            person,
            row.OccurrenceId,
            preparation,
            Payloads.Create(scope, Route, person, row.OccurrenceId, 1)
        );
        return row.OccurrenceId;
    }

    internal static async Task CancelUnfinishedReviewAsync(
        GrowthDatabase database,
        string scope,
        Guid person,
        CancellationToken token
    )
    {
        var row =
            await LockAsync(database, scope, person, token)
            ?? throw new InvalidOperationException("An approved closure anchor is required.");
        // Cancelling an unfinished review does not replace the last approved closure's cycle.
        row.UnfinishedReviewCancelled = true;
    }

    internal static async Task<Acceptance> AcceptAsync(
        GrowthDatabase database,
        string scope,
        OutgoingMessage message,
        DateTimeOffset now,
        CancellationToken token
    )
    {
        NativeDueIntents.RequireInstant(now);
        var payload = Payloads.Read(message, scope, Route);
        var row = await LockAsync(database, scope, payload.SourceId, token);
        if (row is null || row.OccurrenceId != payload.OccurrenceId)
            return Acceptance.Stale;

        if (row.PreparationAt > now)
            return Acceptance.NotDue;

        if (
            await database
                .Set<PreparationIndication>()
                .AnyAsync(
                    value =>
                        value.ScopeKey == scope
                        && value.PersonId == row.PersonId
                        && value.OccurrenceId == row.OccurrenceId,
                    token
                )
        )
            return Acceptance.Duplicate;

        // An indication supplies context for HR; no review is initiated automatically.
        database.Add(
            new PreparationIndication
            {
                ScopeKey = scope,
                PersonId = row.PersonId,
                OccurrenceId = row.OccurrenceId,
            }
        );
        return Acceptance.Accepted;
    }

    private static async Task<ReviewCycle?> LockAsync(
        GrowthDatabase database,
        string scope,
        Guid person,
        CancellationToken token
    )
    {
        NativeDueIntents.RequireTransaction(database);

        var rows = await database
            .Set<ReviewCycle>()
            .FromSqlInterpolated(
                $"SELECT * FROM growth_proof.cycles WHERE scope_key = {scope} AND person_id = {person} FOR UPDATE"
            )
            .ToArrayAsync(token);
        return rows.SingleOrDefault();
    }
}
