using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.RecurrenceProof;

public static class Application
{
    public static async Task Main(string[] args)
    {
        string connection =
            Environment.GetEnvironmentVariable("RECURRENCE_CONNECTION")
            ?? throw new InvalidOperationException("Select a disposable recurrence database.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var key = new RecurringIntentKey(
            "maintenance",
            Guid.Parse("00000000-0000-0000-0000-000000000051"),
            Guid.Parse("00000000-0000-0000-0000-000000000052")
        );
        var anchor = new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
        var definition = RecurrenceDefinition.Every(
            "rotation-v1",
            anchor,
            TimeSpan.FromMinutes(5),
            MissedOccurrences.Skip
        );
        await using var database = new RecurrenceDatabase(
            new DbContextOptionsBuilder<RecurrenceDatabase>().UseNpgsql(connection).Options
        );
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        if (args[0].StartsWith("zone-", StringComparison.Ordinal))
        {
            string path = Path.Combine(
                Environment.GetEnvironmentVariable("TZDIR")!,
                "Gloaming",
                "Test"
            );
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Gloaming/Test");
            string rules = Convert.ToHexStringLower(
                SHA256.HashData(await File.ReadAllBytesAsync(path, token))
            );
            Console.WriteLine(
                "ZONE_OFFSET:"
                    + zone.GetUtcOffset(anchor).TotalMinutes.ToString(CultureInfo.InvariantCulture)
            );
            definition = RecurrenceDefinition.Cron(
                "pinned-host-v1",
                anchor,
                "0 0 * * *",
                RecurrenceCronFormat.FiveFields,
                zone,
                rules,
                MissedOccurrences.Skip
            );
            if (args[0] == "zone-register")
            {
                new PostgresRecurringIntents<RecurrenceDatabase>(database).Register(
                    key,
                    definition
                );
                await database.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return;
            }

            try
            {
                var planned = await new PostgresRecurringIntents<RecurrenceDatabase>(
                    database
                ).PlanAsync(
                    key,
                    definition,
                    anchor,
                    10,
                    occurrence =>
                        new(
                            occurrence.MessageId,
                            "zone-worker",
                            "zone.indicate",
                            1,
                            JsonSerializer.SerializeToElement(new { nominal = occurrence.DueAt })
                        ),
                    token
                );
                await database.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                Console.WriteLine("ZONE_STAGED:" + planned.Staged);
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("INCOMPATIBLE_CALENDAR");
                Environment.ExitCode = 42;
            }

            return;
        }

        var result = await new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
            key,
            definition,
            DateTimeOffset.Parse(args[0], CultureInfo.InvariantCulture),
            10,
            occurrence =>
                new(
                    occurrence.MessageId,
                    "storage-worker",
                    "maintenance.rotate",
                    1,
                    JsonSerializer.SerializeToElement(
                        new { partition = "storage", nominal = occurrence.DueAt }
                    )
                ),
            token
        );
        Console.WriteLine("STAGED:" + result.Staged);
        await PauseAsync("staged", token);
        await database.SaveChangesAsync(token);
        Console.WriteLine("SAVED");
        await PauseAsync("saved", token);
        await transaction.CommitAsync(token);
        Console.WriteLine("COMMITTED");
        await PauseAsync("committed", token);
    }

    private static async Task PauseAsync(string phase, CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("RECURRENCE_PAUSE") != phase)
            return;

        Console.WriteLine("PAUSED:" + phase);
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }
}

public sealed class RecurrenceDatabase(DbContextOptions<RecurrenceDatabase> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigurePostgresOutbox("recurrence", "outbox");
        modelBuilder.ConfigurePostgresDueIntents("recurrence");
        modelBuilder.ConfigurePostgresRecurrence("recurrence");
        modelBuilder
            .Entity<MaintenanceProbe>()
            .ToTable("maintenance_probe", "recurrence")
            .HasKey(row => row.Id);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateRecurrenceChanges();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        await this.ValidateRecurrenceChangesAsync(cancellationToken);

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public sealed class MaintenanceProbe
{
    public int Id { get; set; }
    public string RequiredValue { get; set; } = null!;
}
