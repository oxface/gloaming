using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.ContributionProof;

public enum ImportState
{
    Collecting = 1,
    Ready = 2,
    Expired = 3,
}

public sealed class ImportBatch
{
    public string ScopeKey { get; set; } = null!;
    public Guid Id { get; set; }
    public Guid GenerationId { get; set; }
    public ImportState State { get; set; }
    public int Rows { get; set; }
}

public sealed class ImportDatabase(DbContextOptions<ImportDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigurePostgresOutbox("imports", "outbox");
        modelBuilder.ConfigurePostgresDueIntents("imports");
        modelBuilder.ConfigurePostgresContributions("imports");
        modelBuilder
            .Entity<ImportBatch>()
            .ToTable("batches", "imports")
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

public static class Importing
{
    private static readonly string[] Chunks = ["chunk-1", "chunk-2", "chunk-3"];

    public static void Open(
        ImportDatabase database,
        ContributionSetKey key,
        DateTimeOffset deadline
    )
    {
        database.Add(
            new ImportBatch
            {
                ScopeKey = key.ScopeKey,
                Id = key.WorkId,
                GenerationId = key.GenerationId,
                State = ImportState.Collecting,
            }
        );
        new PostgresContributionSets<ImportDatabase>(database).Register(
            key,
            new(
                Chunks,
                3,
                Notice(key, "imports.results-ready"),
                new(
                    deadline,
                    Notice(key, "imports.timeout-signal"),
                    Notice(key, "imports.results-late")
                )
            )
        );
    }

    public static async Task<ContributionResult> ReceiveAsync(
        ImportDatabase database,
        ContributionSetKey key,
        string chunk,
        int rows,
        CancellationToken token
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);

        _ = await CurrentAsync(database, key, token);
        return await new PostgresContributionSets<ImportDatabase>(database).AddAsync(
            key,
            chunk,
            new("imports.chunk-result", 1, JsonSerializer.SerializeToElement(new { rows })),
            token
        );
    }

    public static async Task<bool> ApplyReadyAsync(
        ImportDatabase database,
        ContributionSetKey key,
        CancellationToken token
    )
    {
        var batch = await CurrentAsync(database, key, token);
        if (batch.State != ImportState.Collecting)
            return false;

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
                "Only a completed selected import generation is ready."
            );

        var results = await database
            .Set<ContributionRecord>()
            .AsNoTracking()
            .Where(row =>
                row.ScopeKey == key.ScopeKey
                && row.WorkId == key.WorkId
                && row.GenerationId == key.GenerationId
            )
            .ToArrayAsync(token);
        if (
            results.Length != 3
            || results.Any(row => row.Name != "imports.chunk-result" || row.SchemaVersion != 1)
        )
            throw new InvalidDataException("The import owns its complete chunk result reader.");

        batch.Rows = results.Sum(row => row.Payload.GetProperty("rows").GetInt32());
        batch.State = ImportState.Ready;
        return true;
    }

    public static async Task<bool> ApplyExpiredAsync(
        ImportDatabase database,
        ContributionSetKey key,
        CancellationToken token
    )
    {
        var batch = await CurrentAsync(database, key, token);
        if (batch.State != ImportState.Collecting)
            return false;

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
        if (head.State != ContributionSetState.TimedOut)
            throw new InvalidOperationException(
                "Only the current timed-out import generation may expire."
            );

        batch.State = ImportState.Expired;
        return true;
    }

    public static async Task<ContributionTimeoutResult> TimeoutAsync(
        ImportDatabase database,
        ContributionSetKey key,
        DateTimeOffset observed,
        CancellationToken token
    )
    {
        _ = await CurrentAsync(database, key, token);
        return await new PostgresContributionSets<ImportDatabase>(database).TimeoutAsync(
            key,
            observed,
            token
        );
    }

    private static async Task<ImportBatch> CurrentAsync(
        ImportDatabase database,
        ContributionSetKey key,
        CancellationToken token
    )
    {
        var rows = await database
            .Set<ImportBatch>()
            .FromSql(
                $"SELECT * FROM imports.batches WHERE \"ScopeKey\" = {key.ScopeKey} AND \"Id\" = {key.WorkId} FOR UPDATE"
            )
            .ToArrayAsync(token);
        if (rows.Length != 1 || rows[0].GenerationId != key.GenerationId)
            throw new InvalidOperationException("The import generation is no longer current.");

        if (!Enum.IsDefined(rows[0].State))
            throw new InvalidDataException("Invalid retained import state.");

        return rows[0];
    }

    private static OutgoingMessage Notice(ContributionSetKey key, string name) =>
        new(
            Guid.NewGuid(),
            "import-coordinator",
            name,
            1,
            JsonSerializer.SerializeToElement(
                new { batchId = key.WorkId, generation = key.GenerationId }
            )
        );
}
