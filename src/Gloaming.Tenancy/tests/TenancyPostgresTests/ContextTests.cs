using System.Data;
using Gloaming.Tenancy.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gloaming.Tenancy.Postgres.Tests;

public sealed class ContextTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IndependentConsumerOwnsScopesTransactionsCommitRollbackAndPhysicalPoolReuse()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        int? backend = null;
        foreach (string partition in new[] { ProofDatabase.Alpha, ProofDatabase.Beta })
        {
            using var tenancy = new TenantContextAccessor();
            var operation = TenantContext.ForTenant(new(partition));
            tenancy.Initialize(operation);
            await using var database = new ConsumerDatabase(proof.Runtime);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            int current = await BackendAsync(database);
            if (backend is not null)
                Assert.Equal(backend, current);

            backend = current;
            Assert.Equal(0, await database.Notes.CountAsync(Token));
            await database.EstablishTenantDatabaseContextAsync(
                tenancy,
                ProofDatabase.Setting,
                Token
            );
            Assert.Same(transaction, database.Database.CurrentTransaction);
            Assert.Same(operation, tenancy.Current);
            var note = Assert.Single(await database.Notes.ToArrayAsync(Token));
            Assert.Equal(partition, note.Partition);
            note.Body = "Changed";
            await database.SaveChangesAsync(Token);
            if (partition == ProofDatabase.Alpha)
                await transaction.CommitAsync(Token);
            else
                await transaction.RollbackAsync(Token);
        }

        await using (var inspect = new ConsumerDatabase(proof.Runtime))
        {
            await using var check = await inspect.Database.BeginTransactionAsync(Token);
            Assert.Equal(backend, await BackendAsync(inspect));
            Assert.Equal(0, await inspect.Notes.CountAsync(Token));
            using var selected = new TenantContextAccessor();
            selected.Initialize(TenantContext.ForTenant(new(ProofDatabase.Beta)));
            await inspect.EstablishTenantDatabaseContextAsync(
                selected,
                ProofDatabase.Setting,
                Token
            );
            Assert.Equal("Beta", (await inspect.Notes.SingleAsync(Token)).Body);
        }

        await using var stored = new ConsumerDatabase(proof.Runtime);
        await using var next = await stored.Database.BeginTransactionAsync(Token);
        using var alpha = new TenantContextAccessor();
        alpha.Initialize(TenantContext.ForTenant(new(ProofDatabase.Alpha)));
        await stored.EstablishTenantDatabaseContextAsync(alpha, ProofDatabase.Setting, Token);
        Assert.Equal(
            "Changed",
            await stored.Notes.AsNoTracking().Select(row => row.Body).SingleAsync(Token)
        );
    }

    [Fact]
    public async Task ConsumerSettingAndOpaqueTenantValueAreParameterizedAndNotAssumed()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        using var tenancy = new TenantContextAccessor();
        tenancy.Initialize(TenantContext.ForTenant(new(ProofDatabase.Alpha)));
        await using var database = new ConsumerDatabase(proof.Runtime);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await database.EstablishTenantDatabaseContextAsync(tenancy, "another.partition_key", Token);
        Assert.Equal(0, await database.Notes.CountAsync(Token));
        await database.EstablishTenantDatabaseContextAsync(tenancy, ProofDatabase.Setting, Token);
        Assert.Equal(ProofDatabase.Alpha, (await database.Notes.SingleAsync(Token)).Partition);
        Assert.Equal(
            ProofDatabase.Alpha,
            await database
                .Database.SqlQueryRaw<string>(
                    "SELECT current_setting('consumer.partition_key') AS \"Value\""
                )
                .SingleAsync(Token)
        );
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tenantless")]
    [InlineData("transaction")]
    public async Task SetupRejectsMissingRequirementsWithoutOpeningOrStartingAnything(
        string missing
    )
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        using var tenancy = new TenantContextAccessor();
        if (missing == "tenantless")
            tenancy.Initialize(TenantContext.Tenantless());
        else if (missing == "transaction")
            tenancy.Initialize(TenantContext.ForTenant(new(ProofDatabase.Alpha)));

        await using var database = new ConsumerDatabase(proof.Runtime);
        if (missing == "tenantless")
            await Assert.ThrowsAsync<TenantRequiredException>(() =>
                database.EstablishTenantDatabaseContextAsync(tenancy, ProofDatabase.Setting, Token)
            );
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.EstablishTenantDatabaseContextAsync(tenancy, ProofDatabase.Setting, Token)
            );

        Assert.Null(database.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, database.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("role")]
    public async Task SettingMustBeExplicitAndQualified(string? setting)
    {
        using var database = new DbContext(new DbContextOptionsBuilder().Options);
        using var tenancy = new TenantContextAccessor();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            database.EstablishTenantDatabaseContextAsync(tenancy, setting!, Token)
        );
    }

    [Fact]
    public async Task ServerSetupFailurePropagatesAndCallerRollsBackBeforeFreshAttempt()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        using var tenancy = new TenantContextAccessor();
        var operation = TenantContext.ForTenant(new(ProofDatabase.Alpha));
        tenancy.Initialize(operation);
        await using (var failed = new ConsumerDatabase(proof.Runtime))
        {
            await using var transaction = await failed.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<PostgresException>(() =>
                failed.Database.ExecuteSqlRawAsync("SELECT 1 / 0", Token)
            );
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                failed.EstablishTenantDatabaseContextAsync(tenancy, ProofDatabase.Setting, Token)
            );
            Assert.Equal(PostgresErrorCodes.InFailedSqlTransaction, error.SqlState);
            Assert.Same(operation, tenancy.Current);
            await transaction.RollbackAsync(Token);
        }

        await using var fresh = new ConsumerDatabase(proof.Runtime);
        await using var attempt = await fresh.Database.BeginTransactionAsync(Token);
        Assert.Equal(0, await fresh.Notes.CountAsync(Token));
        await fresh.EstablishTenantDatabaseContextAsync(tenancy, ProofDatabase.Setting, Token);
        Assert.Equal(1, await fresh.Notes.CountAsync(Token));
    }

    [Fact]
    public async Task CancelledSetupDoesNotAuthorizeTheNextLease()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        using var tenancy = new TenantContextAccessor();
        tenancy.Initialize(TenantContext.ForTenant(new(ProofDatabase.Alpha)));
        await using (var database = new ConsumerDatabase(proof.Runtime))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                database.EstablishTenantDatabaseContextAsync(
                    tenancy,
                    ProofDatabase.Setting,
                    cancelled.Token
                )
            );
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using var next = new ConsumerDatabase(proof.Runtime);
        Assert.Equal(0, await next.Notes.CountAsync(Token));
    }

    private static Task<int> BackendAsync(DbContext database) =>
        database
            .Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(Token);
}
