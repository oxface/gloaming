using System.Data.Common;
using System.Text.Json;
using Gloaming.EntityFrameworkCore;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class DueIntentContractTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Due = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, "accept")]
    [InlineData(true, "accept")]
    [InlineData(false, "detach")]
    [InlineData(true, "detach")]
    [InlineData(false, "clear")]
    [InlineData(true, "clear")]
    public async Task UnsavedOccurrenceCannotDisappearFromItsBusinessSave(
        bool asynchronous,
        string suppression
    )
    {
        string connection = await SetupAsync();
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            new PostgresDueIntents<ContractDatabase>(database).Stage(Key(), Due, Message());
            database.Add(new Effect { Id = 1 });
            if (suppression == "accept")
                database.ChangeTracker.AcceptAllChanges();
            else if (suppression == "detach")
                database.ChangeTracker.Entries<DueIntentRecord>().Single().State =
                    EntityState.Detached;
            else
                database.ChangeTracker.Clear();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SaveAsync(database, asynchronous)
            );
            await transaction.RollbackAsync(Token);
        }

        await ExpectedAsync(connection, 0, 0, 0);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task BothReleasedParticipantsMustSurviveTheSameSave(
        bool asynchronous,
        bool suppressOccurrence
    )
    {
        string connection = await SetupAsync();
        await SeedAsync(connection);
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Single(
                await new PostgresDueIntents<ContractDatabase>(database).ReleaseAsync(
                    "global",
                    Due,
                    1,
                    Token
                )
            );
            database.Add(new Effect { Id = 1 });
            if (suppressOccurrence)
                database.ChangeTracker.Entries<DueIntentRecord>().Single().State =
                    EntityState.Unchanged;
            else
                database.ChangeTracker.Entries<OutboxMessageRecord>().Single().State =
                    EntityState.Unchanged;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SaveAsync(database, asynchronous)
            );
            await transaction.RollbackAsync(Token);
        }

        await ExpectedAsync(connection, 1, 0, 0);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task GenuineStoredHandoffSupportsRepeatedSavesAndExplicitNativeAcceptance(
        bool asynchronous,
        bool acceptOnSuccess
    )
    {
        string connection = await SetupAsync();
        await SeedAsync(connection);
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await new PostgresDueIntents<ContractDatabase>(database).ReleaseAsync(
                "global",
                Due,
                1,
                Token
            );
            Assert.Equal(2, await SaveAsync(database, asynchronous, acceptOnSuccess));
            if (!acceptOnSuccess)
                database.ChangeTracker.AcceptAllChanges();

            Assert.Equal(0, database.SaveChanges());
            Assert.Equal(0, await database.SaveChangesAsync(Token));
            await transaction.CommitAsync(Token);
        }

        await ExpectedAsync(connection, 1, 1, 0, DueIntentState.Released);
        await using var read = Open(connection);
        var outgoing = await read.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Null(outgoing.TenantKey);
        Assert.Equal("global-route", outgoing.RouteKey);
        Assert.Equal("reminder.ready", outgoing.MessageName);
        Assert.Equal(7, outgoing.SchemaVersion);
        Assert.Equal("conversation", outgoing.CorrelationId);
        Assert.Equal("cause", outgoing.CausationId);
        Assert.Equal(
            "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            outgoing.TraceParent
        );
        Assert.Equal("vendor=value", outgoing.TraceState);
        Assert.True(
            JsonElement.DeepEquals(
                JsonSerializer.SerializeToElement(new { literal = 43 }),
                outgoing.Payload
            )
        );
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task StoredVerificationRejectsAnUndoneSavepoint(bool asynchronous, bool release)
    {
        string connection = await SetupAsync();
        await SeedAsync(connection);
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await transaction.CreateSavepointAsync("before", Token);
            var intents = new PostgresDueIntents<ContractDatabase>(database);
            if (release)
                await intents.ReleaseAsync("global", Due, 1, Token);
            else
                Assert.Equal(
                    DueIntentSuppression.Suppressed,
                    await intents.SuppressAsync(Key(), Token)
                );

            await database.SaveChangesAsync(Token);
            await transaction.RollbackToSavepointAsync("before", Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SaveAsync(database, asynchronous)
            );
            await transaction.RollbackAsync(Token);
        }

        await ExpectedAsync(connection, 1, 0, 0);
    }

    [Theory]
    [InlineData(nameof(DueIntentRecord.DueAt))]
    [InlineData(nameof(DueIntentRecord.Payload))]
    [InlineData(nameof(DueIntentRecord.MessageName))]
    [InlineData(nameof(DueIntentRecord.State))]
    public async Task StagedEnvelopeAndLifecycleCannotBeRewritten(string property)
    {
        string connection = await SetupAsync();
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        new PostgresDueIntents<ContractDatabase>(database).Stage(Key(), Due, Message());
        var entry = database.ChangeTracker.Entries<DueIntentRecord>().Single();
        entry.Property(property).CurrentValue = property switch
        {
            nameof(DueIntentRecord.DueAt) => Due.AddDays(1),
            nameof(DueIntentRecord.Payload) => JsonSerializer.SerializeToElement(
                new { literal = 99 }
            ),
            nameof(DueIntentRecord.MessageName) => "different-contract",
            _ => DueIntentState.Released,
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task FailedOrReplacedOperationStateRequiresFreshRecovery()
    {
        string connection = await SetupAsync();
        await using var database = Open(connection);
        var intents = new PostgresDueIntents<ContractDatabase>(database);
        await using (var first = await database.Database.BeginTransactionAsync(Token))
        {
            intents.Stage(Key(), Due, Message());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                intents.SuppressAsync(Key(), Token)
            );
            await first.RollbackAsync(Token);
        }

        await using var replacement = await database.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        await replacement.RollbackAsync(Token);
        await ExpectedAsync(connection, 0, 0, 0);
        await SeedAsync(connection);
        await ExpectedAsync(connection, 1, 0, 0);
    }

    [Fact]
    public async Task ReusedDeliveryIdentityFailsWithoutLosingPendingWork()
    {
        string connection = await SetupAsync();
        Guid delivery = Guid.NewGuid();
        await using (var old = Open(connection))
        {
            await using var transaction = await old.Database.BeginTransactionAsync(Token);
            new EfOutbox<ContractDatabase>(old).Enqueue(Message(delivery));
            await old.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await SeedAsync(connection, delivery);
        await using (var release = Open(connection))
        {
            await using var transaction = await release.Database.BeginTransactionAsync(Token);
            await new PostgresDueIntents<ContractDatabase>(release).ReleaseAsync(
                "global",
                Due,
                1,
                Token
            );
            var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                release.SaveChangesAsync(Token)
            );
            Assert.Equal(
                PostgresErrorCodes.UniqueViolation,
                Assert.IsType<PostgresException>(error.InnerException).SqlState
            );
            await transaction.RollbackAsync(Token);
        }

        await ExpectedAsync(connection, 1, 1, 0);
    }

    [Fact]
    public async Task RegisteredAsyncValidationUsesCancellableNativeReads()
    {
        string connection = await SetupAsync();
        await SeedAsync(connection);
        var interceptor = new AsyncReadsOnly();
        var services = new ServiceCollection();
        services.AddPostgresDueIntents<ContractDatabase>();
        await using var provider = services.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<ContractDatabase>()
            .UseNpgsql(connection)
            .AddInterceptors(interceptor);
        options.ConfigureGloamingCapabilities<ContractDatabase>(provider);
        await using var database = new ContractDatabase(options.Options, manual: false);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new PostgresDueIntents<ContractDatabase>(database).ReleaseAsync(
            "global",
            Due,
            1,
            Token
        );
        await database.SaveChangesAsync(Token);
        int before = interceptor.Reads;
        Assert.Equal(0, await database.SaveChangesAsync(Token));
        Assert.True(interceptor.Reads >= before + 2);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SaveChangesAsync(cancelled.Token)
        );
        await transaction.RollbackAsync(Token);
        await ExpectedAsync(connection, 1, 0, 0);
    }

    [Theory]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("{\"outer\":[{\"x\":1,\"x\":2}]}")]
    [InlineData("{\"x\":1,\"\\u0078\":2}")]
    public async Task AmbiguousJsonCannotBecomeAnImmutableOccurrenceProposal(string json)
    {
        string connection = await SetupAsync();
        using var source = JsonDocument.Parse(json);
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Throws<ArgumentException>(() =>
                new PostgresDueIntents<ContractDatabase>(database).Stage(
                    Key(),
                    Due,
                    new(Guid.NewGuid(), "route", "opaque.content", 1, source.RootElement)
                )
            );
            await transaction.RollbackAsync(Token);
        }

        await ExpectedAsync(connection, 0, 0, 0);
    }

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = Open(connection);
        await database.Database.EnsureCreatedAsync(Token);
        return connection;
    }

    [Theory]
    [InlineData("precision")]
    [InlineData("timestamp-type")]
    [InlineData("filtered-identity")]
    [InlineData("state-token")]
    [InlineData("state-type")]
    [InlineData("column")]
    [InlineData("scope-collation")]
    public void UnsupportedNativeMappingsFailBeforeAnyConnection(string fault)
    {
        var options = new DbContextOptionsBuilder<FaultyDatabase>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused")
            .ReplaceService<IModelCacheKeyFactory, FaultyModelKey>()
            .Options;
        using var database = new FaultyDatabase(options, fault);
        Assert.Throws<InvalidOperationException>(() =>
            new PostgresDueIntents<FaultyDatabase>(database)
        );
    }

    [Fact]
    public void SupportedNativeModelNeedsNoDatabaseConnection()
    {
        var options = new DbContextOptionsBuilder<ContractDatabase>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused")
            .Options;
        using var database = new ContractDatabase(options);
        _ = new PostgresDueIntents<ContractDatabase>(database);
    }

    private static ContractDatabase Open(string connection) =>
        new(new DbContextOptionsBuilder<ContractDatabase>().UseNpgsql(connection).Options);

    private static DueIntentKey Key() =>
        new(
            "global",
            Guid.Parse("00000000-0000-0000-0000-000000000043"),
            Guid.Parse("00000000-0000-0000-0000-000000000044")
        );

    private static OutgoingMessage Message(Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            "global-route",
            "reminder.ready",
            7,
            JsonSerializer.SerializeToElement(new { literal = 43 }),
            correlationId: "conversation",
            causationId: "cause",
            traceParent: "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            traceState: "vendor=value"
        );

    private static async Task SeedAsync(string connection, Guid? delivery = null)
    {
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        new PostgresDueIntents<ContractDatabase>(database).Stage(Key(), Due, Message(delivery));
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static Task<int> SaveAsync(
        ContractDatabase database,
        bool asynchronous,
        bool accept = true
    ) =>
        asynchronous
            ? database.SaveChangesAsync(accept, Token)
            : Task.FromResult(database.SaveChanges(accept));

    private static async Task ExpectedAsync(
        string connection,
        int occurrences,
        int outgoing,
        int effects,
        DueIntentState state = DueIntentState.Pending
    )
    {
        await using var database = Open(connection);
        Assert.Equal(occurrences, await database.Set<DueIntentRecord>().CountAsync(Token));
        Assert.Equal(outgoing, await database.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(effects, await database.Set<Effect>().CountAsync(Token));
        if (occurrences == 1)
            Assert.Equal(state, (await database.Set<DueIntentRecord>().SingleAsync(Token)).State);
    }

    private sealed class ContractDatabase(
        DbContextOptions<ContractDatabase> options,
        bool manual = true
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigurePostgresOutbox("timers", "outbox");
            model.ConfigurePostgresDueIntents("timers");
            model.Entity<Effect>().ToTable("effects", "timers").HasKey(row => row.Id);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            if (manual)
                this.ValidateDueIntentChanges();

            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        )
        {
            if (manual)
                await this.ValidateDueIntentChangesAsync(cancellationToken);

            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private sealed class Effect
    {
        public int Id { get; set; }
    }

    private sealed class FaultyDatabase(DbContextOptions<FaultyDatabase> options, string fault)
        : DbContext(options)
    {
        internal string Fault => fault;

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigurePostgresOutbox("timers", "outbox");
            var row = model.ConfigurePostgresDueIntents("timers");
            switch (fault)
            {
                case "precision":
                    row.Property(value => value.DueAt).HasPrecision(3);
                    break;
                case "timestamp-type":
                    row.Property(value => value.DueAt).HasColumnType("timestamp without time zone");
                    break;
                case "filtered-identity":
                    row.HasIndex(value => value.MessageId).HasFilter("state = 1");
                    break;
                case "state-token":
                    row.Property(value => value.State).IsConcurrencyToken(false);
                    break;
                case "state-type":
                    row.Property(value => value.State).HasColumnType("text");
                    break;
                case "column":
                    row.Property(value => value.MessageName).HasColumnName("different");
                    break;
                case "scope-collation":
                    row.Property(value => value.ScopeKey).UseCollation("default");
                    break;
            }
        }
    }

    private sealed class FaultyModelKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((FaultyDatabase)context).Fault, designTime);
    }

    private sealed class AsyncReadsOnly : DbCommandInterceptor
    {
        internal int Reads { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        ) =>
            throw new InvalidOperationException(
                "This consumer supports asynchronous native reads only."
            );

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }
}
