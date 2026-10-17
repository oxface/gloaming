using System.Data.Common;
using System.Transactions;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace Gloaming.Persistence.Postgres.Tests;

public sealed class RowSecurityTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BeginningTheTransactionEstablishesContextAndEndingItClearsContextOnTheSameBackend()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        int? backend = null;
        Microsoft.EntityFrameworkCore.Metadata.IModel? model = null;
        foreach (
            string partition in new[]
            {
                ProofDatabase.Alpha,
                ProofDatabase.Beta,
                ProofDatabase.Alpha,
            }
        )
        {
            await using var operation = new Operation(proof.Runtime, partition);
            var database = operation.Database;
            if (model is not null)
                Assert.Same(model, database.Model);

            model = database.Model;
            var context = operation.Tenancy.Current;
            await database.Database.OpenConnectionAsync(Token);
            await using (var transaction = await database.Database.BeginTransactionAsync(Token))
            {
                int current = await database
                    .Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
                    .SingleAsync(Token);
                if (backend is not null)
                    Assert.Equal(backend, current);

                backend = current;
                Assert.Equal(
                    partition,
                    (await database.Notes.IgnoreQueryFilters().SingleAsync(Token)).Partition
                );
                Assert.Equal(partition, (await database.Notes.SingleAsync(Token)).Partition);
                Assert.Same(context, operation.Tenancy.Current);
                if (partition == ProofDatabase.Beta)
                    await transaction.RollbackAsync(Token);
                else
                    await transaction.CommitAsync(Token);
            }

            // Observe outside EF interception: commit/rollback cleared local context even
            // though this logical connection is still open and the physical backend is unchanged.
            await using var inspect = new NpgsqlCommand(
                "SELECT NULLIF(current_setting('consumer.partition_key', true), '')",
                (NpgsqlConnection)database.Database.GetDbConnection()
            );
            Assert.IsType<DBNull>(await inspect.ExecuteScalarAsync(Token));
            inspect.CommandText = "SELECT count(*) FROM workspace.\"Owned Notes\"";
            Assert.Equal(0L, await inspect.ExecuteScalarAsync(Token));
            inspect.CommandText =
                "INSERT INTO workspace.\"Owned Notes\" (id, \"Partition\"\"Key\", body) VALUES (@id, @owner, 'Unbound')";
            inspect.Parameters.AddWithValue("id", Guid.NewGuid());
            inspect.Parameters.AddWithValue("owner", partition);
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                inspect.ExecuteNonQueryAsync(Token)
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackedWritesUseAutomaticSetupAndPreserveNativeReturningResults(
        bool asynchronous
    )
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using var operation = new Operation(proof.Runtime, ProofDatabase.Alpha);
        var database = operation.Database;
        await using var transaction = asynchronous
            ? await database.Database.BeginTransactionAsync(Token)
            : database.Database.BeginTransaction();
        var added = new Note { Partition = ProofDatabase.Alpha, Body = "Inserted" };
        database.Notes.Add(added);
        Assert.Equal(
            1,
            asynchronous ? await database.SaveChangesAsync(Token) : database.SaveChanges()
        );
        Assert.Equal(10, added.Revision);
        added.Body = "Updated";
        Assert.Equal(
            1,
            asynchronous ? await database.SaveChangesAsync(Token) : database.SaveChanges()
        );
        database.Remove(added);
        Assert.Equal(
            1,
            asynchronous ? await database.SaveChangesAsync(Token) : database.SaveChanges()
        );
        Assert.Equal(
            1,
            asynchronous
                ? await database.Notes.IgnoreQueryFilters().CountAsync(Token)
                : database.Notes.IgnoreQueryFilters().Count()
        );
        if (asynchronous)
            await transaction.CommitAsync(Token);
        else
            transaction.Commit();
    }

    [Fact]
    public async Task WithCheckRejectsForeignTrackedInsertsEvenWhenConsumerWriteValidationIsFaulty()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using var operation = new Operation(
            proof.Runtime,
            ProofDatabase.Alpha,
            validateWrites: false
        );
        await using var transaction = await operation.Database.Database.BeginTransactionAsync(
            Token
        );
        operation.Database.Add(new Note { Partition = ProofDatabase.Beta, Body = "Forged" });
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            operation.Database.SaveChangesAsync(Token)
        );
        Assert.Equal(
            PostgresErrorCodes.InsufficientPrivilege,
            Assert.IsType<PostgresException>(failure.InnerException).SqlState
        );
    }

    [Fact]
    public async Task ExistingOwnershipValidationStillRejectsForeignTrackedWrites()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using var operation = new Operation(proof.Runtime, ProofDatabase.Alpha);
        await using var transaction = await operation.Database.Database.BeginTransactionAsync(
            Token
        );
        operation.Database.Add(new Note { Partition = ProofDatabase.Beta, Body = "Forged" });
        var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
            operation.Database.SaveChangesAsync(Token)
        );
        Assert.Equal(TenantOwnershipFailure.ForeignOwner, failure.Reason);
    }

    [Fact]
    public async Task BulkAndRawWritesAreLimitedByUsingAndWithCheckAfterFiltersAreDisabled()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using (var operation = new Operation(proof.Runtime, ProofDatabase.Alpha))
        {
            var database = operation.Database;
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                0,
                await database
                    .Notes.IgnoreQueryFilters()
                    .Where(row => row.Id == proof.BetaId)
                    .ExecuteUpdateAsync(
                        change => change.SetProperty(row => row.Body, "Foreign"),
                        Token
                    )
            );
            Assert.Equal(
                0,
                await database
                    .Notes.IgnoreQueryFilters()
                    .Where(row => row.Id == proof.BetaId)
                    .ExecuteDeleteAsync(Token)
            );
            Assert.Equal(
                1,
                await database
                    .Notes.IgnoreQueryFilters()
                    .ExecuteUpdateAsync(
                        change => change.SetProperty(row => row.Body, "Changed"),
                        Token
                    )
            );
            await transaction.CreateSavepointAsync("before_bad_update", Token);
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                database
                    .Notes.IgnoreQueryFilters()
                    .ExecuteUpdateAsync(
                        change => change.SetProperty(row => row.Partition, ProofDatabase.Beta),
                        Token
                    )
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
            await transaction.RollbackToSavepointAsync("before_bad_update", Token);
            await transaction.CreateSavepointAsync("before_bad_insert", Token);
            failure = await Assert.ThrowsAsync<PostgresException>(() =>
                database.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO workspace.\"Owned Notes\" (id, \"Partition\"\"Key\", body) VALUES ({Guid.NewGuid()}, {ProofDatabase.Beta}, 'Forged')",
                    Token
                )
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
            await transaction.RollbackToSavepointAsync("before_bad_insert", Token);
            Assert.Equal(
                0,
                await database.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE workspace.\"Owned Notes\" SET body = 'Foreign' WHERE id = {proof.BetaId}",
                    Token
                )
            );
            Assert.Equal(1, await database.Notes.IgnoreQueryFilters().ExecuteDeleteAsync(Token));
            await transaction.CommitAsync(Token);
        }

        await using var beta = new Operation(proof.Runtime, ProofDatabase.Beta);
        await using var read = await beta.Database.Database.BeginTransactionAsync(Token);
        Assert.Equal("Beta", (await beta.Database.Notes.SingleAsync(Token)).Body);
    }

    [Fact]
    public async Task SavepointsRollbackAndFreshTransactionsRetainTheFixedOperationTenant()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using var operation = new Operation(proof.Runtime, ProofDatabase.Alpha);
        var database = operation.Database;
        await database.Database.OpenConnectionAsync(Token);
        await using (var transaction = await database.Database.BeginTransactionAsync(Token))
        {
            Assert.Same(transaction, database.Database.CurrentTransaction);
            await transaction.CreateSavepointAsync("before_work", Token);
            Assert.Equal(
                1,
                await database.Notes.ExecuteUpdateAsync(
                    change => change.SetProperty(row => row.Body, "Uncommitted"),
                    Token
                )
            );
            await transaction.RollbackToSavepointAsync("before_work", Token);
            Assert.Equal(
                "Alpha",
                (await database.Notes.AsNoTracking().IgnoreQueryFilters().SingleAsync(Token)).Body
            );
            await transaction.RollbackAsync(Token);
        }

        await using var next = await database.Database.BeginTransactionAsync(Token);
        Assert.Equal(1, await database.Notes.IgnoreQueryFilters().CountAsync(Token));
        await database.Notes.ExecuteUpdateAsync(
            change => change.SetProperty(row => row.Body, "Committed"),
            Token
        );
        await next.CommitAsync(Token);
        await using var read = await database.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            "Committed",
            (await database.Notes.AsNoTracking().IgnoreQueryFilters().SingleAsync(Token)).Body
        );
    }

    [Theory]
    [InlineData("read")]
    [InlineData("save")]
    [InlineData("bulk")]
    [InlineData("raw")]
    public async Task ProtectedWorkRequiresTheExistingOwnerToBeginATransaction(string path)
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using var operation = new Operation(proof.Runtime, ProofDatabase.Alpha);
        var database = operation.Database;
        if (path == "save")
            database.Add(new Note { Partition = ProofDatabase.Alpha, Body = "Not saved" });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (path == "save")
                await database.SaveChangesAsync(Token);
            else if (path == "bulk")
                await database.Notes.IgnoreQueryFilters().ExecuteDeleteAsync(Token);
            else if (path == "raw")
                await database.Database.ExecuteSqlRawAsync(
                    "DELETE FROM workspace.\"Owned Notes\"",
                    Token
                );
            else
                await database.Notes.IgnoreQueryFilters().CountAsync(Token);
        });
        Assert.Contains("transaction", failure.Message, StringComparison.Ordinal);
        Assert.Null(database.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenantless")]
    public async Task MissingOrTenantlessContextRejectsSetupAndDisposesTheUnpublishedNativeTransaction(
        string? selected
    )
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using var operation = new Operation(proof.Runtime, selected);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            operation.Database.Database.BeginTransactionAsync(Token)
        );
        Assert.Null(operation.Database.Database.CurrentTransaction);
        // The failed BeginTransaction did not leave a native transaction behind.
        await using var native = await (
            (NpgsqlConnection)operation.Database.Database.GetDbConnection()
        ).BeginTransactionAsync(Token);
        await native.RollbackAsync(Token);
    }

    [Fact]
    public async Task MissingUseRlsIsDetectedBeforeSavingAndMissingRegistrationFailsAtConfiguration()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using (
            var operation = new Operation(proof.Runtime, ProofDatabase.Alpha, installRls: false)
        )
        {
            await using var transaction = await operation.Database.Database.BeginTransactionAsync(
                Token
            );
            operation.Database.Add(
                new Note { Partition = ProofDatabase.Alpha, Body = "Not saved" }
            );
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                operation.Database.SaveChangesAsync(Token)
            );
            Assert.Contains("UseRls", failure.Message, StringComparison.Ordinal);
        }

        using var provider = new ServiceCollection().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<ConsumerDatabase>().UseNpgsql(proof.Runtime);
        Assert.Throws<InvalidOperationException>(() => options.UseRls<ConsumerDatabase>(provider));
    }

    [Fact]
    public async Task CancellationDuringTransactionSetupRollsBackBeforeFreshOperationRetry()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var hook = new CancelAfterStarting(cancelled);
        await using (
            var operation = new Operation(
                proof.Runtime,
                ProofDatabase.Alpha,
                configure: options => options.AddInterceptors(hook)
            )
        )
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                operation.Database.Database.BeginTransactionAsync(cancelled.Token)
            );
            Assert.NotNull(hook.Native);
            Assert.Throws<ObjectDisposedException>(() => hook.Native.Connection);
            Assert.Null(operation.Database.Database.CurrentTransaction);
            Assert.True(hook.WasUnpublished);
        }

        await using var retry = new Operation(proof.Runtime, ProofDatabase.Beta);
        await using var transaction = await retry.Database.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            ProofDatabase.Beta,
            (await retry.Database.Notes.IgnoreQueryFilters().SingleAsync(Token)).Partition
        );
    }

    [Fact]
    public async Task AuthorizingSessionDefaultsAreRejectedBeforeBusinessWork()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await proof.AuthorizingRoleDefaultAsync(Token);
        await using var operation = new Operation(proof.Runtime, ProofDatabase.Alpha);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.Database.Database.BeginTransactionAsync(Token)
        );
        Assert.Contains("baseline", failure.Message, StringComparison.Ordinal);
        Assert.Null(operation.Database.Database.CurrentTransaction);
    }

    [Fact]
    public async Task PreopenedConnectionWorksButAdoptedTransactionRemainsCallerOwnedAndIsRejected()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using var external = new NpgsqlConnection(proof.Runtime);
        await external.OpenAsync(Token);
        await using var operation = new Operation(
            proof.Runtime,
            ProofDatabase.Alpha,
            configure: options => options.UseNpgsql(external)
        );
        await using (
            var transaction = await operation.Database.Database.BeginTransactionAsync(Token)
        )
        {
            Assert.Equal(1, await operation.Database.Notes.IgnoreQueryFilters().CountAsync(Token));
            await transaction.CommitAsync(Token);
        }

        await using var native = await external.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.Database.Database.UseTransactionAsync(native, Token)
        );
        Assert.Same(external, native.Connection);
        Assert.Null(operation.Database.Database.CurrentTransaction);
        await native.RollbackAsync(Token);
    }

    [Fact]
    public async Task AmbientTransactionsAndUnsafePoolModesAreRejected()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        foreach (string mode in new[] { "reset", "multiplex", "ambient" })
        {
            var settings = new NpgsqlConnectionStringBuilder(proof.Runtime)
            {
                NoResetOnClose = mode == "reset",
                Multiplexing = mode == "multiplex",
            };
            await using var operation = new Operation(
                settings.ConnectionString,
                ProofDatabase.Alpha
            );
            using var ambient =
                mode == "ambient"
                    ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)
                    : null;
            var failure = await Record.ExceptionAsync(() =>
                operation.Database.Database.BeginTransactionAsync(Token)
            );
            Assert.True(failure is InvalidOperationException or NotSupportedException);
            Assert.Null(operation.Database.Database.CurrentTransaction);
            using var pool = new NpgsqlConnection(settings.ConnectionString);
            NpgsqlConnection.ClearPool(pool);
        }
    }

    [Fact]
    public async Task DirectNativeCommandsOnTheOwningTransactionReceiveReadAndWritePolicies()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using var operation = new Operation(proof.Runtime, ProofDatabase.Alpha);
        await using var transaction = await operation.Database.Database.BeginTransactionAsync(
            Token
        );
        var connection = (NpgsqlConnection)operation.Database.Database.GetDbConnection();
        await using (
            var foreign = new NpgsqlCommand(
                "UPDATE workspace.\"Owned Notes\" SET body = 'Foreign' WHERE id = @id",
                connection,
                (NpgsqlTransaction)transaction.GetDbTransaction()
            )
        )
        {
            foreign.Parameters.AddWithValue("id", proof.BetaId);
            Assert.Equal(0, await foreign.ExecuteNonQueryAsync(Token));
        }

        await transaction.CreateSavepointAsync("before_bad_write", Token);
        await using (
            var transfer = new NpgsqlCommand(
                "UPDATE workspace.\"Owned Notes\" SET \"Partition\"\"Key\" = @foreign",
                connection,
                (NpgsqlTransaction)transaction.GetDbTransaction()
            )
        )
        {
            transfer.Parameters.AddWithValue("foreign", ProofDatabase.Beta);
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                transfer.ExecuteNonQueryAsync(Token)
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        }

        await transaction.RollbackToSavepointAsync("before_bad_write", Token);
        Assert.Equal(1, await operation.Database.Notes.IgnoreQueryFilters().CountAsync(Token));
        await transaction.CommitAsync(Token);
    }

    [Fact]
    public async Task DatabaseTenantComparisonStaysExactWithACaseInsensitiveColumnCollation()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await proof.PoliciesAsync(false, Token);
        await proof.AdminSqlAsync(
            """
            CREATE COLLATION workspace.case_insensitive (provider = icu, locale = 'und-u-ks-level2', deterministic = false);
            ALTER TABLE workspace."Owned Notes" ALTER COLUMN "Partition""Key" TYPE text COLLATE workspace.case_insensitive;
            INSERT INTO workspace."Owned Notes" (id, "Partition""Key", body) VALUES
                ('00000000-0000-0000-0000-000000000001', 'TENANT:CASE', 'Upper'),
                ('00000000-0000-0000-0000-000000000002', 'tenant:case', 'Lower');
            """,
            Token
        );
        await proof.PoliciesAsync(true, Token);
        await using var operation = new Operation(proof.Runtime, "tenant:case");
        await using var transaction = await operation.Database.Database.BeginTransactionAsync(
            Token
        );
        Assert.Equal("Lower", (await operation.Database.Notes.SingleAsync(Token)).Body);
        Assert.Equal(
            "tenant:case",
            (await operation.Database.Notes.IgnoreQueryFilters().SingleAsync(Token)).Partition
        );
    }

    [Fact]
    public async Task UnselectedContextExecutesWithoutTenancyOrTransaction()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        var services = new ServiceCollection();
        services.AddPostgresTenantRowSecurity<PlainDatabase>();
        using var provider = services.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<PlainDatabase>().UseNpgsql(proof.Runtime);
        options.UseRls<PlainDatabase>(provider);
        await using var database = new PlainDatabase(options.Options);
        Assert.Equal(
            42,
            await database.Database.SqlQueryRaw<int>("SELECT 42 AS \"Value\"").SingleAsync(Token)
        );
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            42,
            await database.Database.SqlQueryRaw<int>("SELECT 42 AS \"Value\"").SingleAsync(Token)
        );
    }

    [Fact]
    public async Task PolicyHelpersRoundTripAndActualRuntimeRoleHasNoBypass()
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using var runtime = new NpgsqlConnection(proof.Runtime);
        await runtime.OpenAsync(Token);
        await using var check = new NpgsqlCommand(
            """
            SELECT NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreaterole
                AND NOT r.rolcreatedb AND current_user = session_user
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT pg_has_role(r.oid, c.relowner, 'MEMBER')
                AND c.relrowsecurity AND c.relforcerowsecurity
                AND (SELECT count(*) FROM pg_policy WHERE polrelid = c.oid) = 1
                AND (SELECT polqual IS NOT NULL AND polwithcheck IS NOT NULL FROM pg_policy WHERE polrelid = c.oid)
                AND NOT has_schema_privilege(current_user, 'workspace', 'CREATE')
            FROM pg_roles r CROSS JOIN pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE r.rolname = current_user AND n.nspname = 'workspace' AND c.relname = 'Owned Notes'
            """,
            runtime
        );
        Assert.Equal(true, await check.ExecuteScalarAsync(Token));
        await runtime.CloseAsync();
        await proof.PoliciesAsync(false, Token);
        await using (var disabled = new Operation(proof.Runtime, ProofDatabase.Alpha))
        {
            await using var transaction = await disabled.Database.Database.BeginTransactionAsync(
                Token
            );
            Assert.Equal(2, await disabled.Database.Notes.IgnoreQueryFilters().CountAsync(Token));
        }

        await proof.PoliciesAsync(true, Token);
        await using var enabled = new Operation(proof.Runtime, ProofDatabase.Alpha);
        await using var read = await enabled.Database.Database.BeginTransactionAsync(Token);
        Assert.Equal(1, await enabled.Database.Notes.IgnoreQueryFilters().CountAsync(Token));
    }

    private sealed class PlainDatabase(DbContextOptions<PlainDatabase> options)
        : DbContext(options);

    private sealed class CancelAfterStarting(CancellationTokenSource source)
        : DbTransactionInterceptor
    {
        internal DbTransaction? Native { get; private set; }
        internal bool WasUnpublished { get; private set; }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default
        )
        {
            Native = result;
            WasUnpublished = eventData.Context!.Database.CurrentTransaction is null;
            source.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
