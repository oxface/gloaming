using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo;

// Consumer-owned diagnostic records; no customer snapshot or serving view is constructed.
public sealed class FeedJournalDbContext(DbContextOptions<FeedJournalDbContext> options)
    : DbContext(options)
{
    internal DbSet<SubscriptionRow> Subscriptions => Set<SubscriptionRow>();
    internal DbSet<ReceiptRow> Receipts => Set<ReceiptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("feed_journal");
        var subscription = modelBuilder.Entity<SubscriptionRow>();
        subscription.ToTable(
            "subscriptions",
            table =>
                table.HasCheckConstraint(
                    "journal_progress",
                    "\"Position\" >= 0 AND \"Version\" > 0"
                )
        );
        subscription.HasKey(row => row.Id);
        subscription.Property(row => row.Id).ValueGeneratedNever();
        subscription.Property(row => row.Organization).HasMaxLength(256);
        subscription.Property(row => row.Version).IsConcurrencyToken();

        var receipt = modelBuilder.Entity<ReceiptRow>();
        receipt.ToTable(
            "receipts",
            table => table.HasCheckConstraint("journal_position", "\"Position\" > 0")
        );
        receipt.HasKey(row => new
        {
            row.SubscriptionId,
            row.Generation,
            row.ChangeId,
        });
        receipt
            .HasIndex(row => new
            {
                row.SubscriptionId,
                row.Generation,
                row.Position,
            })
            .IsUnique();
        receipt.Property(row => row.Payload).HasColumnType("jsonb");
        receipt
            .HasOne<SubscriptionRow>()
            .WithMany()
            .HasForeignKey(row => row.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubscriptionRow
{
    public Guid Id { get; set; }
    public required string Organization { get; set; }
    public Guid Generation { get; set; }
    public long Position { get; set; }
    public long Version { get; set; } = 1;
}

internal sealed class ReceiptRow
{
    public Guid SubscriptionId { get; set; }
    public Guid Generation { get; set; }
    public Guid ChangeId { get; set; }
    public long Position { get; set; }
    public JsonElement Payload { get; set; }
}
