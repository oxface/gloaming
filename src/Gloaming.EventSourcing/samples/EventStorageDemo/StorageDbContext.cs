using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.EventStorageDemo;

public sealed class StorageDbContext(DbContextOptions<StorageDbContext> options)
    : DbContext(options)
{
    public DbSet<EventStreamRecord> Streams => Set<EventStreamRecord>();
    public DbSet<StoredEventRecord> Events => Set<StoredEventRecord>();

    public static DbContextOptions<StorageDbContext> Options(string connection) =>
        new DbContextOptionsBuilder<StorageDbContext>()
            .UseNpgsql(
                connection,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "journal")
            )
            .Options;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureEventSourcingStorage<EventStreamRecord, StoredEventRecord>(
            new EventSourcingStorageOptions
            {
                Schema = "journal",
                StreamsTable = "streams",
                EventsTable = "facts",
            }
        );
        modelBuilder
            .Entity<EventStreamRecord>()
            .Property(row => row.Description)
            .HasColumnName("description")
            .HasMaxLength(128);
        modelBuilder
            .Entity<StoredEventRecord>()
            .Property(row => row.Payload)
            .HasColumnType("jsonb");
    }
}
