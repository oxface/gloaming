using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModulithFoundry.Samples.EventReactions;

public sealed class DirectOrdersModule { }

public sealed class DirectOrder : OwnedEntity
{
    private DirectOrder() { }

    public PendingEvents Events { get; } = new();

    public Guid Id { get; private set; }
    public decimal Amount { get; private set; }
    public bool Submitted { get; private set; }
    public long Version { get; private set; }

    public static DirectOrder Draft(string owner, Guid id, decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        return new()
        {
            Owner = owner,
            Id = id,
            Amount = amount,
            Version = 1,
        };
    }

    public void Submit()
    {
        if (Submitted)
            throw new InvalidOperationException("This order was already submitted.");

        Submitted = true;
        Version = checked(Version + 1);
        Events.Raise(new OrderSubmitted(Id, Amount));
    }

    public void Delete()
    {
        if (!Submitted)
            throw new InvalidOperationException(
                "This example deletes previously submitted orders."
            );

        Events.Raise(new OrderDeleted(Id, Amount));
    }
}

public sealed class DirectAllowance : OwnedEntity
{
    private DirectAllowance() { }

    public PendingEvents Events { get; } = new();

    public decimal Ceiling { get; private set; }
    public decimal Reserved { get; private set; }
    public long Version { get; private set; }

    public static DirectAllowance Open(string owner, decimal ceiling)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceiling);

        return new()
        {
            Owner = owner,
            Ceiling = ceiling,
            Version = 1,
        };
    }

    public void Reserve(Guid order, decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        decimal next = checked(Reserved + amount);
        if (next > Ceiling)
            throw new AllowanceExceededException(Ceiling, next);

        Reserved = next;
        Version = checked(Version + 1);
        Events.Raise(new AllowanceAllocated(order, amount));
    }

    public void Release(decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        if (amount > Reserved)
            throw new InvalidOperationException(
                "The allocation cannot exceed the reserved allowance."
            );

        Reserved -= amount;
        Version = checked(Version + 1);
    }
}

public sealed class DirectReceipt
{
    public required string Owner { get; set; }
    public Guid OrderId { get; set; }
}

public sealed class DirectAllocation
{
    public required string Owner { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
}

public sealed class DirectOrdersContext : DbContext
{
    public DirectOrdersContext(DbContextOptions<DirectOrdersContext> options)
        : base(options) { }

    public string Owner { get; } = "alpha";
    public DbSet<DirectOrder> Orders => Set<DirectOrder>();
    public DbSet<DirectAllowance> Allowances => Set<DirectAllowance>();
    public DbSet<DirectReceipt> Receipts => Set<DirectReceipt>();
    public DbSet<DirectAllocation> Allocations => Set<DirectAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureEventReactions();
        modelBuilder.HasDefaultSchema("direct_orders");
        var order = modelBuilder.Entity<DirectOrder>();
        order.HasBaseType((Type?)null);
        order.ToTable("orders");
        order.Ignore(row => row.Events);
        order.HasKey(row => new { row.Owner, row.Id });
        order.Property(row => row.Id).ValueGeneratedNever();
        order.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        order.HasQueryFilter(row => row.Owner == Owner);
        var allowance = modelBuilder.Entity<DirectAllowance>();
        allowance.HasBaseType((Type?)null);
        allowance.ToTable("allowances");
        allowance.Ignore(row => row.Events);
        allowance.HasKey(row => row.Owner);
        allowance.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        allowance.HasQueryFilter(row => row.Owner == Owner);
        var receipt = modelBuilder.Entity<DirectReceipt>();
        receipt.ToTable("receipts");
        receipt.HasKey(row => new { row.Owner, row.OrderId });
        receipt.HasQueryFilter(row => row.Owner == Owner);
        receipt
            .HasOne<DirectOrder>()
            .WithMany()
            .HasForeignKey(row => new { row.Owner, row.OrderId })
            .OnDelete(DeleteBehavior.Restrict);
        var allocation = modelBuilder.Entity<DirectAllocation>();
        allocation.ToTable("allocations");
        allocation.HasKey(row => new { row.Owner, row.OrderId });
        allocation.HasQueryFilter(row => row.Owner == Owner);
        allocation
            .HasOne<DirectOrder>()
            .WithMany()
            .HasForeignKey(row => new { row.Owner, row.OrderId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public static class DirectOrdersDatabase
{
    public static DbContextOptions<DirectOrdersContext> Options(string connection) =>
        new DbContextOptionsBuilder<DirectOrdersContext>()
            .UseNpgsql(
                connection,
                postgres =>
                    postgres.MigrationsHistoryTable("__EFMigrationsHistory", "direct_orders")
            )
            .Options;
}

public sealed class DirectOrdersDesignFactory : IDesignTimeDbContextFactory<DirectOrdersContext>
{
    public DirectOrdersContext CreateDbContext(string[] args) =>
        new(
            DirectOrdersDatabase.Options(
                "Host=localhost;Database=not_opened;Username=not_used;Password=not_used"
            )
        );
}
