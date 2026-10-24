using System.Data.Common;
using System.Text.Json;
using Gloaming.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class SaveValidationCompositionTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Anchor = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredRecurrenceAndContributionsReadEachStoredParticipantOnce(
        bool asynchronous
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var services = new ServiceCollection();
        services.AddPostgresRecurrence<CompositionDatabase>();
        services.AddPostgresContributions<CompositionDatabase>();
        var reads = new ReadProbe();
        services.AddDbContext<CompositionDatabase>(
            (provider, options) =>
                options
                    .UseNpgsql(connection)
                    .AddInterceptors(reads)
                    .ConfigureGloamingCapabilities<CompositionDatabase>(provider)
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        var recurring = new RecurringIntentKey("maintenance", Guid.NewGuid(), Guid.NewGuid());
        var set = new ContributionSetKey("contributions", Guid.NewGuid(), Guid.NewGuid());
        var definition = RecurrenceDefinition.Every(
            "five-minute-v1",
            Anchor,
            TimeSpan.FromMinutes(5),
            MissedOccurrences.Skip
        );
        var completion = Message(Guid.NewGuid(), "consumer.completed");
        await using (var scope = provider.CreateAsyncScope())
        {
            var seed = scope.ServiceProvider.GetRequiredService<CompositionDatabase>();
            await seed.Database.EnsureCreatedAsync(Token);
            await using var transaction = await seed.Database.BeginTransactionAsync(Token);
            new PostgresRecurringIntents<CompositionDatabase>(seed).Register(recurring, definition);
            new PostgresContributionSets<CompositionDatabase>(seed).Register(
                set,
                new(
                    ["contributor"],
                    1,
                    completion,
                    new(
                        Anchor.AddHours(1),
                        Message(Guid.NewGuid(), "consumer.timeout-signal"),
                        Message(Guid.NewGuid(), "consumer.timed-out")
                    )
                )
            );
            await seed.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<CompositionDatabase>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var planned = await new PostgresRecurringIntents<CompositionDatabase>(
                database
            ).PlanAsync(
                recurring,
                definition,
                Anchor,
                10,
                occurrence => Message(occurrence.MessageId, "maintenance.rotate"),
                Token
            );
            Assert.Equal(1, planned.Staged);
            Assert.Equal(
                ContributionResult.Completed,
                await new PostgresContributionSets<CompositionDatabase>(database).AddAsync(
                    set,
                    "contributor",
                    new(
                        "consumer.content",
                        1,
                        JsonSerializer.SerializeToElement(new { value = 17 })
                    ),
                    Token
                )
            );
            await SaveAsync(database, asynchronous);
            reads.Commands.Clear();
            Assert.Equal(0, await SaveAsync(database, asynchronous));

            // Two finite rows, one recurring generation and the contribution head/content/notice.
            Assert.Equal(6, reads.Commands.Count);
            Assert.Equal(6, reads.Commands.Distinct(StringComparer.Ordinal).Count());
            await transaction.CommitAsync(Token);
        }

        await using var readScope = provider.CreateAsyncScope();
        var read = readScope.ServiceProvider.GetRequiredService<CompositionDatabase>();
        Assert.Equal(
            new[] { DueIntentState.Pending, DueIntentState.Cancelled },
            await read.Set<DueIntentRecord>()
                .OrderBy(row => row.State)
                .Select(row => row.State)
                .ToArrayAsync(Token)
        );
        Assert.Equal(
            ContributionSetState.Completed,
            (await read.Set<ContributionSetRecord>().SingleAsync(Token)).State
        );
        Assert.Equal(
            17,
            (await read.Set<ContributionRecord>().SingleAsync(Token))
                .Payload.GetProperty("value")
                .GetInt32()
        );
        Assert.Equal(
            completion.MessageId,
            (await read.Set<OutboxMessageRecord>().SingleAsync(Token)).MessageId
        );
        Assert.Single(await read.Set<RecurringIntentRecord>().ToArrayAsync(Token));
    }

    private static OutgoingMessage Message(Guid id, string name) =>
        new(id, "worker", name, 1, JsonSerializer.SerializeToElement(new { retained = true }));

    private static Task<int> SaveAsync(CompositionDatabase database, bool asynchronous) =>
        asynchronous ? database.SaveChangesAsync(Token) : Task.FromResult(database.SaveChanges());

    private sealed class CompositionDatabase(DbContextOptions<CompositionDatabase> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigurePostgresOutbox("composition", "outbox");
            model.ConfigurePostgresDueIntents("composition");
            model.ConfigurePostgresRecurrence("composition");
            model.ConfigurePostgresContributions("composition");
        }
    }

    private sealed class ReadProbe : DbCommandInterceptor
    {
        internal List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command) =>
            Commands.Add(
                command.CommandText
                    + string.Join(
                        "|",
                        command
                            .Parameters.Cast<DbParameter>()
                            .Select(parameter => $"{parameter.ParameterName}={parameter.Value}")
                    )
            );
    }
}
