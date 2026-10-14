using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace Gloaming.Auditing.Tests;

public sealed class TransactionTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NullableSubjectIsRetainedAndItemTimelineExcludesOtherItemsAndGlobalEntries()
    {
        string connection = await SetupAsync();
        Guid earlier = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid later = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await using (var database = AuditDatabase.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var audit = new EfAudit<AuditDatabase>(database);
            audit.Stage(AuditConsumer.Entry(later));
            audit.Stage(AuditConsumer.Entry(earlier));
            audit.Stage(AuditConsumer.Entry(subjectKey: "document:other"));
            audit.Stage(
                AuditConsumer.Entry(
                    attribution: new ActorContext(Actor.System(new ActorId("maintenance"))),
                    subjectKey: null
                )
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using var read = AuditDatabase.Open(connection);
        var index = await read
            .Database.SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value" FROM pg_indexes
                WHERE schemaname = 'operations' AND tablename = 'accepted_log'
                  AND indexname = 'ix_audit_subject_timeline'
                """
            )
            .SingleAsync(Token);
        Assert.Contains("(tenant_key, subject_type, subject_key, occurred_at, audit_id)", index);
        var timeline = await read.Set<AuditRecord>()
            .Where(row =>
                row.TenantKey == null
                && row.SubjectType == "document"
                && row.SubjectKey == "document:9"
            )
            .OrderBy(row => row.OccurredAt)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(Token);
        Assert.Equal([earlier, later], timeline);
        var global = await read.Set<AuditRecord>()
            .SingleAsync(row => row.SubjectKey == null, Token);
        Assert.Equal(ActorKind.System, global.ActorKind);
        Assert.Null(global.SubjectKey);
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("dispose")]
    public async Task StageDoesNotSaveAndOnlyExplicitCommitPersistsBusinessAndAudit(string ending)
    {
        string connection = await SetupAsync();
        await using (var database = AuditDatabase.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            new EfAudit<AuditDatabase>(database).Stage(
                AuditConsumer.Entry(reasonCode: "customer-request")
            );
            database.Add(new BusinessRow { Id = Guid.NewGuid(), Name = "Before" });
            Assert.Equal((0, 0), await CountsAsync(connection));

            await database.SaveChangesAsync(Token);
            Assert.Equal((0, 0), await CountsAsync(connection));
            (await database.Set<BusinessRow>().SingleAsync(Token)).Name = "After";
            await database.SaveChangesAsync(Token);
            if (ending == "commit")
                await transaction.CommitAsync(Token);
            else if (ending == "rollback")
                await transaction.RollbackAsync(Token);
        }

        Assert.Equal(ending == "commit" ? (1, 1) : (0, 0), await CountsAsync(connection));
        if (ending == "commit")
        {
            await using var read = AuditDatabase.Open(connection);
            Assert.Equal("After", (await read.Set<BusinessRow>().SingleAsync(Token)).Name);
            var audit = await read.Set<AuditRecord>().SingleAsync(Token);
            Assert.Equal(ActorKind.Human, audit.ActorKind);
            Assert.Equal("person:7", audit.ActorKey);
            Assert.Null(audit.InitiatorKind);
            Assert.Null(audit.TenantKey);
            Assert.Equal("customer-request", audit.ReasonCode);
            Assert.Equal(7, audit.OccurredAt.Hour);
            Assert.Equal(4, audit.Details.GetProperty("Version").GetInt32());
        }
    }

    [Fact]
    public async Task EqualActorKeysKeepKindsDistinctAndRetainOptionalInitiator()
    {
        string connection = await SetupAsync();
        await using (var database = AuditDatabase.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var audit = new EfAudit<AuditDatabase>(database);
            audit.Stage(
                AuditConsumer.Entry(attribution: new ActorContext(Actor.Human(new ActorId("same"))))
            );
            audit.Stage(
                AuditConsumer.Entry(
                    attribution: new ActorContext(
                        Actor.System(new ActorId("same")),
                        Actor.Human(new ActorId("original"))
                    )
                )
            );
            audit.Stage(
                AuditConsumer.Entry(attribution: new ActorContext(Actor.Anonymous, Actor.Anonymous))
            );
            // Exercise the consumer's synchronous override too.
            database.SaveChanges();
            await transaction.CommitAsync(Token);
        }

        await using var read = AuditDatabase.Open(connection);
        var rows = await read.Set<AuditRecord>().ToArrayAsync(Token);
        Assert.Equal(3, rows.Length);
        Assert.Null(Assert.Single(rows, row => row.ActorKind == ActorKind.Human).InitiatorKind);
        var worker = Assert.Single(rows, row => row.ActorKind == ActorKind.System);
        Assert.Equal("same", worker.ActorKey);
        Assert.Equal(ActorKind.Human, worker.InitiatorKind);
        Assert.Equal("original", worker.InitiatorKey);
        var anonymous = Assert.Single(rows, row => row.ActorKind == ActorKind.Anonymous);
        Assert.Null(anonymous.ActorKey);
        Assert.Equal(ActorKind.Anonymous, anonymous.InitiatorKind);
        Assert.Null(anonymous.InitiatorKey);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("business")]
    [InlineData("cancel")]
    public async Task FaultAfterEarlierSaveRollsBackAndFreshContextRecovers(string fault)
    {
        string connection = await SetupAsync();
        if (fault == "audit")
        {
            await using var setup = AuditDatabase.Open(connection);
            await setup.Database.ExecuteSqlRawAsync(
                "ALTER TABLE operations.accepted_log ADD CONSTRAINT fault CHECK (schema_version > 2)",
                Token
            );
        }

        await using (var database = AuditDatabase.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            database.Add(new BusinessRow { Id = Guid.NewGuid(), Name = "First save" });
            await database.SaveChangesAsync(Token);
            new EfAudit<AuditDatabase>(database).Stage(AuditConsumer.Entry());
            if (fault == "business")
                (await database.Set<BusinessRow>().SingleAsync(Token)).Name = "";

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            if (fault == "cancel")
                await cancellation.CancelAsync();

            if (fault == "cancel")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    database.SaveChangesAsync(cancellation.Token)
                );
            else
                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    database.SaveChangesAsync(cancellation.Token)
                );
            await transaction.RollbackAsync(CancellationToken.None);
        }

        Assert.Equal((0, 0), await CountsAsync(connection));
        await using var recovered = AuditDatabase.Open(connection);
        if (fault == "audit")
            await recovered.Database.ExecuteSqlRawAsync(
                "ALTER TABLE operations.accepted_log DROP CONSTRAINT fault",
                Token
            );

        await using var retry = await recovered.Database.BeginTransactionAsync(Token);
        recovered.Add(new BusinessRow { Id = Guid.NewGuid(), Name = "Recovered" });
        new EfAudit<AuditDatabase>(recovered).Stage(AuditConsumer.Entry());
        await recovered.SaveChangesAsync(Token);
        await retry.CommitAsync(Token);
        Assert.Equal((1, 1), await CountsAsync(connection));
    }

    [Fact]
    public async Task DuplicateAuditIdentityFailsNativeUniquenessAndIsNotAnIdempotentStage()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await using (var first = AuditDatabase.Open(connection))
        {
            await using var transaction = await first.Database.BeginTransactionAsync(Token);
            new EfAudit<AuditDatabase>(first).Stage(AuditConsumer.Entry(id));
            await first.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using (var second = AuditDatabase.Open(connection))
        {
            await using var transaction = await second.Database.BeginTransactionAsync(Token);
            new EfAudit<AuditDatabase>(second).Stage(AuditConsumer.Entry(id));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                second.SaveChangesAsync(Token)
            );
            Assert.Equal(
                PostgresErrorCodes.UniqueViolation,
                Assert.IsType<PostgresException>(failure.InnerException).SqlState
            );
        }

        Assert.Equal((0, 1), await CountsAsync(connection));
    }

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = AuditDatabase.Open(connection);
        await database.Database.EnsureCreatedAsync(Token);
        return connection;
    }

    private static async Task<(int Business, int Audit)> CountsAsync(string connection)
    {
        await using var read = AuditDatabase.Open(connection);
        return (
            await read.Set<BusinessRow>().CountAsync(Token),
            await read.Set<AuditRecord>().CountAsync(Token)
        );
    }
}
