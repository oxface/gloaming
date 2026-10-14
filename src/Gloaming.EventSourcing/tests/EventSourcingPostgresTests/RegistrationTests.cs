using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void AggregateRolesUseSeparateScopedImplementationsAndTypedContexts()
    {
        var services = Services();
        services.AddEventStore<Ledger, LedgerRegistrationStore, LedgerRegistrationRebuilder>();
        services.AddEventStore<OtherAggregate, OtherStore, OtherRebuilder>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var ledger = first.ServiceProvider.GetRequiredService<LedgerRegistrationStore>();
        Assert.Same(ledger, first.ServiceProvider.GetRequiredService<IEventStore<Ledger>>());
        Assert.Same(
            first.ServiceProvider.GetRequiredService<LedgerRegistrationRebuilder>(),
            first.ServiceProvider.GetRequiredService<IAggregateRebuilder<Ledger>>()
        );
        Assert.Same(ledger.Database, first.ServiceProvider.GetRequiredService<LedgerDatabase>());
        Assert.Same(TimeProvider.System, ledger.Clock);
        Assert.NotSame(ledger, second.ServiceProvider.GetRequiredService<IEventStore<Ledger>>());
        var other = first.ServiceProvider.GetRequiredService<OtherStore>();
        Assert.Same(other, first.ServiceProvider.GetRequiredService<IEventStore<OtherAggregate>>());
        Assert.Same(
            first.ServiceProvider.GetRequiredService<OtherRebuilder>(),
            first.ServiceProvider.GetRequiredService<IAggregateRebuilder<OtherAggregate>>()
        );
        Assert.Same(other.Database, first.ServiceProvider.GetRequiredService<OtherDatabase>());
        Assert.NotSame(ledger.Database, other.Database);
        Assert.Null(first.ServiceProvider.GetService<DbContext>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatRegistrationPreservesClockOverrides(bool overrideAfter)
    {
        var services = Services();
        var clock = new Clock();
        if (!overrideAfter)
            services.AddSingleton<TimeProvider>(clock);
        services.AddEventStore<Ledger, LedgerRegistrationStore>();
        int count = services.Count;
        services.AddEventStore<Ledger, LedgerRegistrationStore>();
        Assert.Equal(count, services.Count);
        if (overrideAfter)
            services.AddSingleton<TimeProvider>(clock);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.Same(
            clock,
            scope.ServiceProvider.GetRequiredService<LedgerRegistrationStore>().Clock
        );
        Assert.Single(scope.ServiceProvider.GetServices<IEventStore<Ledger>>());
        Assert.Empty(scope.ServiceProvider.GetServices<IAggregateRebuilder<Ledger>>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritingAndMaintenanceCanBeRegisteredIndependently(bool maintenance)
    {
        var services = Services();
        if (maintenance)
            services.AddAggregateRebuilder<Ledger, LedgerRegistrationRebuilder>();
        else
            services.AddEventStore<Ledger, LedgerRegistrationStore>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.Equal(
            maintenance,
            scope.ServiceProvider.GetService<IAggregateRebuilder<Ledger>>() is not null
        );
        Assert.Equal(
            !maintenance,
            scope.ServiceProvider.GetService<IEventStore<Ledger>>() is not null
        );
    }

    [Fact]
    public void MaintenanceCanInjectAScopedHistoryReaderWithoutRegisteringWriting()
    {
        var services = Services();
        services.AddScoped<LedgerHistoryReader>();
        services.AddScoped<IEventHistoryReader<Movement, LedgerStream>>(provider =>
            provider.GetRequiredService<LedgerHistoryReader>()
        );
        services.AddAggregateRebuilder<Ledger, ReaderRegistrationRebuilder>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var rebuilder = first.ServiceProvider.GetRequiredService<ReaderRegistrationRebuilder>();

        Assert.Same(first.ServiceProvider.GetRequiredService<LedgerDatabase>(), rebuilder.Database);
        Assert.Same(
            first.ServiceProvider.GetRequiredService<LedgerHistoryReader>(),
            rebuilder.Reader
        );
        Assert.NotSame(
            rebuilder.Reader,
            second.ServiceProvider.GetRequiredService<IEventHistoryReader<Movement, LedgerStream>>()
        );
        Assert.Null(first.ServiceProvider.GetService<IEventStore<Ledger>>());
        Assert.Null(first.ServiceProvider.GetService<DbContext>());
    }

    [Fact]
    public void AStreamFamilyCannotRegisterTwoAggregateStates()
    {
        var model = new ModelBuilder();
        model.ConfigureEventSourcingStorage<LedgerStream, StoredEventRecord>(
            new() { Schema = "ledger" }
        );
        var balance = model.Entity<BalanceRow>();
        balance.ToTable("balances", "ledger");
        balance.HasKey(row => row.StreamId);
        balance.HasOne<LedgerStream>().WithMany().HasForeignKey(row => row.StreamId);
        balance.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        var alternate = model.Entity<AlternateState>();
        balance.Property(row => row.RecordedAt);
        alternate.ToTable("alternate", "ledger");
        alternate.HasKey(row => row.StreamId);
        alternate.HasOne<LedgerStream>().WithMany().HasForeignKey(row => row.StreamId);
        alternate.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        alternate.Property(row => row.RecordedAt);
        model.ConfigureRequiredInlineState<LedgerStream, StoredEventRecord, BalanceRow>("ledger");
        var original = model.Model.FindEntityType(typeof(LedgerStream))!.GetAnnotations().ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            model.ConfigureRequiredInlineState<LedgerStream, StoredEventRecord, AlternateState>(
                "ledger"
            )
        );
        Assert.Equal(original, model.Model.FindEntityType(typeof(LedgerStream))!.GetAnnotations());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LedgerDatabase>(options =>
            options.UseNpgsql("Host=localhost;Database=not_opened")
        );
        services.AddDbContext<OtherDatabase>(options =>
            options.UseNpgsql("Host=localhost;Database=not_opened")
        );
        return services;
    }

    private sealed class ReaderRegistrationRebuilder(
        LedgerDatabase database,
        IEventHistoryReader<Movement, LedgerStream> reader
    )
        : AggregateRebuilder<Ledger, Movement, LedgerStream, BalanceRow>(
            database,
            "ledger",
            new LedgerStateMapping(),
            reader
        )
    {
        public LedgerDatabase Database { get; } = database;
        public IEventHistoryReader<Movement, LedgerStream> Reader { get; } = reader;

        protected override Ledger Rehydrate(LedgerStream stream, IReadOnlyList<Movement> events) =>
            new(stream.Id, stream.Version, Ledger.Fold(events));
    }

    private sealed class Clock : TimeProvider;

    private sealed class AlternateState : IInlineStateRecord
    {
        public Guid StreamId { get; set; }
        public long Version { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
    }

    private sealed class OtherAggregate;

    private sealed class OtherDatabase(DbContextOptions<OtherDatabase> options)
        : DbContext(options);

    private sealed class LedgerRegistrationStore(LedgerDatabase database, TimeProvider clock)
        : RegistrationStore<Ledger>(database, clock);

    private sealed class OtherStore(OtherDatabase database, TimeProvider clock)
        : RegistrationStore<OtherAggregate>(database, clock);

    // The tests exercise container identity and ownership; these doubles perform no event I/O.
    private abstract class RegistrationStore<TAggregate>(DbContext database, TimeProvider clock)
        : IEventStore<TAggregate>
        where TAggregate : class
    {
        public DbContext Database { get; } = database;
        public TimeProvider Clock { get; } = clock;

        public Task<TAggregate?> GetForWritingAsync(
            Guid id,
            long? expectedVersion = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<EventAppendResult> AppendAsync(
            TAggregate aggregate,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class LedgerRegistrationRebuilder(LedgerDatabase database)
        : RegistrationRebuilder<Ledger>(database);

    private sealed class OtherRebuilder(OtherDatabase database)
        : RegistrationRebuilder<OtherAggregate>(database);

    private abstract class RegistrationRebuilder<TAggregate>(DbContext database)
        : IAggregateRebuilder<TAggregate>
        where TAggregate : class
    {
        public DbContext Database { get; } = database;

        public Task<AggregateRebuildResult?> RebuildAsync(
            Guid id,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
