using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.ContributionProof;

public enum OfferState
{
    Collecting = 1,
    Selected = 2,
    Rejected = 3,
}

public sealed class OfferRequest
{
    public string ScopeKey { get; set; } = null!;
    public Guid Id { get; set; }
    public Guid GenerationId { get; set; }
    public OfferState State { get; set; }
    public decimal Budget { get; set; }
    public decimal? SelectedPrice { get; set; }
}

public sealed class OfferDatabase(DbContextOptions<OfferDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigurePostgresOutbox("offers", "outbox");
        modelBuilder.ConfigurePostgresDueIntents("offers");
        modelBuilder.ConfigurePostgresContributions("offers");
        modelBuilder
            .Entity<OfferRequest>()
            .ToTable("requests", "offers")
            .HasKey(row => new { row.ScopeKey, row.Id });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateContributionChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        await this.ValidateContributionChangesAsync(cancellationToken);

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public static class Offers
{
    private static readonly string[] Vendors = ["alpha", "beta", "gamma"];

    public static void Open(OfferDatabase database, ContributionSetKey key, decimal budget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget);

        database.Add(
            new OfferRequest
            {
                ScopeKey = key.ScopeKey,
                Id = key.WorkId,
                GenerationId = key.GenerationId,
                State = OfferState.Collecting,
                Budget = budget,
            }
        );
        // This consumer chooses two replies from three vendors. Technical completion is
        // a readiness indication; its own budget/selection rule below may still reject them.
        new PostgresContributionSets<OfferDatabase>(database).Register(
            key,
            new(
                Vendors,
                2,
                new(
                    Guid.NewGuid(),
                    "offer-coordinator",
                    "offers.ready",
                    1,
                    JsonSerializer.SerializeToElement(
                        new { requestId = key.WorkId, generation = key.GenerationId }
                    ),
                    key.ScopeKey
                ),
                null
            )
        );
    }

    public static async Task<ContributionResult> ReceiveAsync(
        OfferDatabase database,
        ContributionSetKey key,
        string vendor,
        decimal price,
        CancellationToken token
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);

        _ = await CurrentAsync(database, key, token);
        return await new PostgresContributionSets<OfferDatabase>(database).AddAsync(
            key,
            vendor,
            new("offers.price", 1, JsonSerializer.SerializeToElement(new { price })),
            token
        );
    }

    public static async Task<OfferState> ApplyReadyAsync(
        OfferDatabase database,
        ContributionSetKey key,
        CancellationToken token
    )
    {
        var request = await CurrentAsync(database, key, token);
        if (request.State != OfferState.Collecting)
            return request.State;

        var head = await database
            .Set<ContributionSetRecord>()
            .AsNoTracking()
            .SingleAsync(
                row =>
                    row.ScopeKey == key.ScopeKey
                    && row.WorkId == key.WorkId
                    && row.GenerationId == key.GenerationId,
                token
            );
        if (head.State != ContributionSetState.Completed)
            throw new InvalidOperationException(
                "Only a technically completed current offer generation is ready."
            );

        var replies = await database
            .Set<ContributionRecord>()
            .AsNoTracking()
            .Where(row =>
                row.ScopeKey == key.ScopeKey
                && row.WorkId == key.WorkId
                && row.GenerationId == key.GenerationId
            )
            .ToArrayAsync(token);
        if (
            replies.Length != 2
            || replies.Any(row => row.Name != "offers.price" || row.SchemaVersion != 1)
        )
            throw new InvalidDataException("The offer consumer owns its price reader.");

        decimal lowest = replies.Min(row => row.Payload.GetProperty("price").GetDecimal());
        request.State = lowest <= request.Budget ? OfferState.Selected : OfferState.Rejected;
        request.SelectedPrice = request.State == OfferState.Selected ? lowest : null;
        return request.State;
    }

    private static async Task<OfferRequest> CurrentAsync(
        OfferDatabase database,
        ContributionSetKey key,
        CancellationToken token
    )
    {
        var rows = await database
            .Set<OfferRequest>()
            .FromSql(
                $"SELECT * FROM offers.requests WHERE \"ScopeKey\" = {key.ScopeKey} AND \"Id\" = {key.WorkId} FOR UPDATE"
            )
            .ToArrayAsync(token);
        if (rows.Length != 1 || rows[0].GenerationId != key.GenerationId)
            throw new InvalidOperationException("The offer generation is no longer current.");

        if (!Enum.IsDefined(rows[0].State))
            throw new InvalidDataException("Invalid retained offer state.");

        return rows[0];
    }
}
