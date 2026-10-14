using System.Text.Json;
using Gloaming.Auditing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Tests.Infrastructure;

namespace Gloaming.Auditing.Tests;

public sealed class GuardTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingJsonIsComparedByExactCapturedTextInsteadOfBackingStorageOrDeepEquality(
        bool sameText
    )
    {
        string connection = await SetupAsync();
        await using var database = AuditDatabase.Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var entry = AuditConsumer.Entry();
        new EfAudit<AuditDatabase>(database).Stage(entry);
        var row = database.ChangeTracker.Entries<AuditRecord>().Single();
        using var replacement = JsonDocument.Parse(
            sameText ? entry.Details.GetRawText() : "{ \"Version\": 4 }"
        );
        Assert.True(JsonElement.DeepEquals(entry.Details, replacement.RootElement));
        Assert.NotEqual(entry.Details, replacement.RootElement);
        row.Property(item => item.Details).CurrentValue = replacement.RootElement.Clone();
        if (!sameText)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
            await transaction.RollbackAsync(Token);
            await AssertEmptyAsync(connection);
            return;
        }

        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await using var read = AuditDatabase.Open(connection);
        Assert.Equal(
            4,
            (await read.Set<AuditRecord>().SingleAsync(Token))
                .Details.GetProperty("Version")
                .GetInt32()
        );
    }

    [Fact]
    public void MissingModelAndMissingTransactionFailWithoutDatabaseIo()
    {
        const string unreachable = "Host=localhost;Port=1;Database=unused;Username=unused";
        using var bare = new BareDatabase(
            new DbContextOptionsBuilder<BareDatabase>().UseNpgsql(unreachable).Options
        );
        Assert.Throws<InvalidOperationException>(() => new EfAudit<BareDatabase>(bare));
        using var mapped = AuditDatabase.Open(unreachable);
        Assert.Throws<InvalidOperationException>(() =>
            new EfAudit<AuditDatabase>(mapped).Stage(AuditConsumer.Entry())
        );
        Assert.Empty(mapped.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedEnvelopeFailsBothNativeSaveOverrides(bool synchronous)
    {
        string connection = await SetupAsync();
        await using var database = AuditDatabase.Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        new EfAudit<AuditDatabase>(database).Stage(AuditConsumer.Entry());
        var row = database.ChangeTracker.Entries<AuditRecord>().Single();
        row.Property(item => item.Action).CurrentValue = "substituted";

        if (synchronous)
            Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );

        await transaction.RollbackAsync(Token);
        await AssertEmptyAsync(connection);
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("dispose")]
    [InlineData("commit")]
    [InlineData("rollback")]
    public async Task AuditCannotBeSavedOrStagedInAnEndedOrReplacedTransaction(string ending)
    {
        string connection = await SetupAsync();
        await using var database = AuditDatabase.Open(connection);
        var transaction = await database.Database.BeginTransactionAsync(Token);
        new EfAudit<AuditDatabase>(database).Stage(AuditConsumer.Entry());
        if (ending == "commit")
            await transaction.CommitAsync(Token);
        else if (ending != "dispose")
            await transaction.RollbackAsync(Token);

        await transaction.DisposeAsync();
        await using var replacement =
            ending == "replace" ? await database.Database.BeginTransactionAsync(Token) : null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        if (ending != "replace")
            Assert.Throws<InvalidOperationException>(() =>
                new EfAudit<AuditDatabase>(database).Stage(AuditConsumer.Entry())
            );

        await AssertEmptyAsync(connection);
    }

    [Fact]
    public async Task CompletedTransactionIsRejectedEvenBeforeNativeWrapperDisposal()
    {
        string connection = await SetupAsync();
        await using var database = AuditDatabase.Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var audit = new EfAudit<AuditDatabase>(database);
        audit.Stage(AuditConsumer.Entry());
        await transaction.CommitAsync(Token);
        Assert.Throws<InvalidOperationException>(() => audit.Stage(AuditConsumer.Entry()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        await AssertEmptyAsync(connection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetachedAuditDoesNotAcquireAnotherContextsStagingEvidence(
        bool differentContextType
    )
    {
        string connection = await SetupAsync();
        await using var first = AuditDatabase.Open(connection);
        await using var firstTransaction = await first.Database.BeginTransactionAsync(Token);
        new EfAudit<AuditDatabase>(first).Stage(AuditConsumer.Entry());
        var row = first.ChangeTracker.Entries<AuditRecord>().Single().Entity;
        first.Entry(row).State = EntityState.Detached;
        await using DbContext second = differentContextType
            ? new AlternateAuditDatabase(
                new DbContextOptionsBuilder<AlternateAuditDatabase>().UseNpgsql(connection).Options
            )
            : AuditDatabase.Open(connection);
        await using var secondTransaction = await second.Database.BeginTransactionAsync(Token);
        second.Add(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.SaveChangesAsync(Token));
        await AssertEmptyAsync(connection);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("edit")]
    [InlineData("delete")]
    public async Task UnstagedInsertsAndTrackedEditsOrDeletesFail(string mutation)
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await using (var seed = AuditDatabase.Open(connection))
        {
            await using var transaction = await seed.Database.BeginTransactionAsync(Token);
            new EfAudit<AuditDatabase>(seed).Stage(AuditConsumer.Entry(id));
            await seed.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using var database = AuditDatabase.Open(connection);
        await using var attempt = await database.Database.BeginTransactionAsync(Token);
        var row = await database.Set<AuditRecord>().SingleAsync(Token);
        if (mutation == "add")
        {
            database.Entry(row).State = EntityState.Detached;
            database.Add(row);
        }
        else if (mutation == "edit")
            database.Entry(row).Property(item => item.Outcome).CurrentValue = "changed";
        else
            database.Remove(row);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        await attempt.RollbackAsync(Token);
        await using var read = AuditDatabase.Open(connection);
        Assert.Equal("accepted", (await read.Set<AuditRecord>().SingleAsync(Token)).Outcome);
    }

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = AuditDatabase.Open(connection);
        await database.Database.EnsureCreatedAsync(Token);
        return connection;
    }

    private static async Task AssertEmptyAsync(string connection)
    {
        await using var read = AuditDatabase.Open(connection);
        Assert.Empty(await read.Set<AuditRecord>().ToArrayAsync(Token));
    }
}
