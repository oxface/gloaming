using Gloaming.EntityFrameworkCore;
using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.EventReactions.Consumer.Tests;

public sealed class AdHocSourceTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedRequestBufferAndOperationSinkParticipateWithoutTrackedEventSources(
        bool synchronous
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var services = new ServiceCollection();
        services.AddScoped<ImportRun>();
        services.AddEventReactions<ImportContext, ImportModule>(reactions =>
            reactions
                .CollectSources((provider, _) => [provider.GetRequiredService<ImportRun>().Events])
                .AddReaction<ImportNoteWritten, RecordImportNote>()
        );
        services.AddDbContext<ImportContext>(
            (provider, options) =>
                options.UseNpgsql(connection).ConfigureGloamingCapabilities<ImportContext>(provider)
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ImportContext>();
            await database.Database.EnsureCreatedAsync(Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var request = scope.ServiceProvider.GetRequiredService<ImportRun>();
            request.Events.Raise(new ImportNoteWritten(Guid.NewGuid(), "request completed"));
            scope
                .ServiceProvider.GetRequiredService<IEventSink<ImportModule>>()
                .Raise(new ImportNoteWritten(Guid.NewGuid(), "operation completed"));
            Assert.Empty(database.ChangeTracker.Entries());
            if (synchronous)
                database.SaveChanges();
            else
                await database.SaveChangesAsync(Token);

            Assert.Equal(0, request.Events.Count);
            await transaction.CommitAsync(Token);
        }

        await using var observer = provider.CreateAsyncScope();
        var read = observer.ServiceProvider.GetRequiredService<ImportContext>();
        Assert.Equal(
            ["operation completed", "request completed"],
            await read.Set<ImportNote>()
                .OrderBy(row => row.Text)
                .Select(row => row.Text)
                .ToArrayAsync(Token)
        );
    }

    private sealed class ImportModule;

    // These ordinary consumer types implement no event/source interfaces.
    private sealed class ImportRun
    {
        internal PendingEvents Events { get; } = new();
    }

    private readonly record struct ImportNoteWritten(Guid Id, string Text);

    private sealed class ImportNote
    {
        public Guid Id { get; set; }
        public required string Text { get; set; }
    }

    private sealed class RecordImportNote(ImportContext database)
        : IEventReaction<ImportNoteWritten>,
            ISynchronousEventReaction<ImportNoteWritten>
    {
        public void Handle(ImportNoteWritten fact) =>
            database.Add(new ImportNote { Id = fact.Id, Text = fact.Text });

        public Task HandleAsync(ImportNoteWritten fact, CancellationToken cancellationToken)
        {
            Handle(fact);
            return Task.CompletedTask;
        }
    }

    private sealed class ImportContext(DbContextOptions<ImportContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEventReactions();
            modelBuilder.Entity<ImportNote>().HasKey(row => row.Id);
            modelBuilder.Entity<ImportNote>().Property(row => row.Id).ValueGeneratedNever();
        }
    }
}
