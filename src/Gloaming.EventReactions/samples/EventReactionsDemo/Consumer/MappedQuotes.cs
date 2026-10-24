using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gloaming.Samples.EventReactions;

public sealed class MappedQuotesModule { }

public sealed class QuoteRow
{
    public Guid Id { get; set; }
    public required string Owner { get; set; }
    public decimal Amount { get; set; }
    public bool Accepted { get; set; }
    public long Version { get; set; }

    public QuoteDomain ToDomain() => new(Id, Amount, Accepted, Version);
}

public sealed class QuoteAllowanceRow
{
    public required string Owner { get; set; }
    public decimal Ceiling { get; set; }
    public decimal Reserved { get; set; }
    public long Version { get; set; }

    internal AllowanceDomain ToDomain() => new(Ceiling, Reserved, Version);

    internal void Apply(AllowanceDomain accepted)
    {
        Reserved = accepted.Reserved;
        Version = accepted.Version;
    }
}

public sealed class QuoteReceipt
{
    public required string Owner { get; set; }
    public Guid OrderId { get; set; }
}

public sealed class QuoteAllocation
{
    public required string Owner { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
}

public sealed class MappedQuotesContext : DbContext
{
    public MappedQuotesContext(DbContextOptions<MappedQuotesContext> options)
        : base(options) { }

    public string Owner { get; } = "alpha";
    public DbSet<QuoteRow> Quotes => Set<QuoteRow>();
    public DbSet<QuoteAllowanceRow> Allowances => Set<QuoteAllowanceRow>();
    public DbSet<QuoteReceipt> Receipts => Set<QuoteReceipt>();
    public DbSet<QuoteAllocation> Allocations => Set<QuoteAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureEventReactions();
        modelBuilder.HasDefaultSchema("mapped_quotes");
        var quote = modelBuilder.Entity<QuoteRow>();
        quote.ToTable("quotes");
        quote.HasKey(row => new { row.Owner, row.Id });
        quote.Property(row => row.Id).ValueGeneratedNever();
        quote.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        quote.HasQueryFilter(row => row.Owner == Owner);
        var allowance = modelBuilder.Entity<QuoteAllowanceRow>();
        allowance.ToTable("allowances");
        allowance.HasKey(row => row.Owner);
        allowance.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        allowance.HasQueryFilter(row => row.Owner == Owner);
        var receipt = modelBuilder.Entity<QuoteReceipt>();
        receipt.ToTable("receipts");
        receipt.HasKey(row => new { row.Owner, row.OrderId });
        receipt.HasQueryFilter(row => row.Owner == Owner);
        receipt
            .HasOne<QuoteRow>()
            .WithMany()
            .HasForeignKey(row => new { row.Owner, row.OrderId })
            .OnDelete(DeleteBehavior.Restrict);
        var allocation = modelBuilder.Entity<QuoteAllocation>();
        allocation.ToTable("allocations");
        allocation.HasKey(row => new { row.Owner, row.OrderId });
        allocation.HasQueryFilter(row => row.Owner == Owner);
        allocation
            .HasOne<QuoteRow>()
            .WithMany()
            .HasForeignKey(row => new { row.Owner, row.OrderId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public static class MappedQuotesDatabase
{
    public static DbContextOptions<MappedQuotesContext> Options(string connection) =>
        new DbContextOptionsBuilder<MappedQuotesContext>()
            .UseNpgsql(
                connection,
                postgres =>
                    postgres.MigrationsHistoryTable("__EFMigrationsHistory", "mapped_quotes")
            )
            .Options;
}

public sealed class MappedQuotesDesignFactory : IDesignTimeDbContextFactory<MappedQuotesContext>
{
    public MappedQuotesContext CreateDbContext(string[] args) =>
        new(
            MappedQuotesDatabase.Options(
                "Host=localhost;Database=not_opened;Username=not_used;Password=not_used"
            )
        );
}
