using Gloaming.EntityFrameworkCore;
using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.EventReactions.Consumer.Tests;

public sealed class EventOrderingTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterleavedReservationsReleaseCapacityBeforeTheNextReservation(
        bool synchronous
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var services = new ServiceCollection();
        services.AddEventReactions<ReservationsContext, Module>(events =>
            events
                .CollectFrom<ReservationBatch>(batch => batch.Events)
                .AddReaction<ReservationChanged, UpdateBudget>()
        );
        services.AddDbContext<ReservationsContext>(
            (provider, options) =>
                options
                    .UseNpgsql(connection)
                    .ConfigureGloamingCapabilities<ReservationsContext>(provider)
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        Guid firstId;
        Guid secondId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ReservationsContext>();
            await database.Database.EnsureCreatedAsync(Token);
            var budget = new Budget { Id = Guid.NewGuid(), Reserved = 40 };
            var first = ReservationBatch.Open(budget.Id, 0);
            var second = ReservationBatch.Open(budget.Id, 40);
            firstId = first.Id;
            secondId = second.Id;
            database.Add(first);
            database.Add(second);
            database.Add(budget);
            await database.SaveChangesAsync(Token);

            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            // Flattening the first root's events would attempt 40 + 60 + 40 and reject
            // a valid operation. Capture order is +60, -40, +40, finishing at 100.
            first.Change(60);
            second.Change(-40);
            first.Change(40);
            if (synchronous)
                database.SaveChanges();
            else
                await database.SaveChangesAsync(Token);

            await transaction.CommitAsync(Token);
        }

        await using var observer = provider.CreateAsyncScope();
        var read = observer.ServiceProvider.GetRequiredService<ReservationsContext>();
        Assert.Equal(100, (await read.Set<Budget>().AsNoTracking().SingleAsync(Token)).Reserved);
        Assert.Equal(
            100,
            (
                await read.Set<ReservationBatch>()
                    .AsNoTracking()
                    .SingleAsync(row => row.Id == firstId, Token)
            ).Amount
        );
        Assert.Equal(
            0,
            (
                await read.Set<ReservationBatch>()
                    .AsNoTracking()
                    .SingleAsync(row => row.Id == secondId, Token)
            ).Amount
        );
    }

    private sealed class Module;

    private sealed record ReservationChanged(Guid BudgetId, decimal Delta);

    private sealed class ReservationBatch
    {
        public PendingEvents Events { get; } = new();
        public Guid Id { get; private set; }
        public Guid BudgetId { get; private set; }
        public decimal Amount { get; private set; }

        internal static ReservationBatch Open(Guid budget, decimal amount) =>
            new()
            {
                Id = Guid.NewGuid(),
                BudgetId = budget,
                Amount = amount,
            };

        internal void Change(decimal delta)
        {
            if (Amount + delta < 0)
                throw new InvalidOperationException("A batch cannot release more than it holds.");

            Amount += delta;
            Events.Raise(new ReservationChanged(BudgetId, delta));
        }
    }

    private sealed class Budget
    {
        public Guid Id { get; set; }
        public decimal Reserved { get; set; }

        internal void Adjust(decimal delta)
        {
            decimal next = Reserved + delta;
            if (next is < 0 or > 100)
                throw new InvalidOperationException("The budget cannot accept this reservation.");

            Reserved = next;
        }
    }

    private sealed class UpdateBudget(ReservationsContext database)
        : IEventReaction<ReservationChanged>,
            ISynchronousEventReaction<ReservationChanged>
    {
        public void Handle(ReservationChanged fact) =>
            database.Set<Budget>().Single(row => row.Id == fact.BudgetId).Adjust(fact.Delta);

        public async Task HandleAsync(ReservationChanged fact, CancellationToken cancellationToken)
        {
            var budget = await database
                .Set<Budget>()
                .SingleAsync(row => row.Id == fact.BudgetId, cancellationToken);
            budget.Adjust(fact.Delta);
        }
    }

    private sealed class ReservationsContext(DbContextOptions<ReservationsContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEventReactions();
            var batch = modelBuilder.Entity<ReservationBatch>();
            batch.HasBaseType((Type?)null);
            batch.Ignore(row => row.Events);
            batch.HasKey(row => row.Id);
            batch.Property(row => row.Id).ValueGeneratedNever();
            var budget = modelBuilder.Entity<Budget>();
            budget.HasKey(row => row.Id);
            budget.Property(row => row.Id).ValueGeneratedNever();
            budget.Property(row => row.Reserved).IsConcurrencyToken();
        }
    }
}
