using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Gloaming.Samples.EventReactions.Consumer.Tests;

public sealed partial class DispatchTests
{
    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task NativeSyncWriteFailureRollsBackWithTheCallerTransaction(string shape)
    {
        string connection = await SetupAsync(shape);
        string schema = shape == "direct" ? "direct_orders" : "mapped_quotes";
        await ExecuteAsync(
            connection,
            $"ALTER TABLE {schema}.allowances ADD CONSTRAINT reject_reservation CHECK (\"Reserved\" = 0)"
        );
        await using var operation = new Scenario(shape, connection);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        Assert.Throws<DbUpdateException>(() => operation.Context.SaveChanges());
        Assert.Equal(0, operation.PendingCount);
        Assert.Same(transaction, operation.Context.Database.CurrentTransaction);
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task NativeSaveCancellationCanBeRolledBackByTheCaller(string shape)
    {
        string connection = await SetupAsync(shape);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var probe = new CancelNativeWrite(cancellation);
        await using var operation = new Scenario(shape, connection, interceptors: [probe]);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            operation.Context.SaveChangesAsync(cancellation.Token)
        );
        Assert.True(probe.Invoked);
        Assert.Equal(0, operation.PendingCount);
        Assert.Same(transaction, operation.Context.Database.CurrentTransaction);
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct", false)]
    [InlineData("direct", true)]
    [InlineData("mapped", false)]
    [InlineData("mapped", true)]
    public async Task CallerTransactionCanRollBackWhenALaterCompletionCallbackFails(
        string shape,
        bool synchronous
    )
    {
        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(
            shape,
            connection,
            interceptors: [new RejectCompletion()]
        );
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await Assert.ThrowsAsync<CompletionRejectedException>(() =>
            SaveAsync(operation.Context, synchronous ? "sync" : "async")
        );
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task DifferentProducerRootsStillCompeteOnTheRequiredAllowancesVersion(string shape)
    {
        string connection = await SetupAsync(shape, 60, 60);
        await using var first = new Scenario(shape, connection);
        await using var second = new Scenario(shape, connection);
        await using var firstTx = await first.Context.Database.BeginTransactionAsync(Token);
        await using var secondTx = await second.Context.Database.BeginTransactionAsync(Token);
        await first.SubmitAsync(First, Token);
        await second.SubmitAsync(Second, Token);
        await first.LoadAllowanceAsync(Token);
        await second.LoadAllowanceAsync(Token);
        await first.Context.SaveChangesAsync(Token);
        await firstTx.CommitAsync(Token);
        var failure = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            second.Context.SaveChangesAsync(Token)
        );
        Assert.Equal("allowances", Assert.Single(failure.Entries).Metadata.GetTableName());
        await secondTx.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, true, 60, 2, 1);
        await using var read = new Scenario(shape, connection);
        Assert.False((await read.ReadAsync(Second, Token))!.Submitted);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task ARequiredHandlerRejectsTheCombinedBatchWithoutPersistingEarlierEffects(
        string shape
    )
    {
        string connection = await SetupAsync(shape, 40, 70);
        await using (var operation = new Scenario(shape, connection))
        {
            await using var transaction = await operation.Context.Database.BeginTransactionAsync(
                Token
            );
            await operation.SubmitAsync(First, Token);
            await operation.SubmitAsync(Second, Token);
            var failure = await Assert.ThrowsAsync<AllowanceExceededException>(() =>
                operation.Context.SaveChangesAsync(Token)
            );
            Assert.Equal(110, failure.Requested);
            // The batch was cleared before dispatch; recovery uses fresh state.
        }

        await AssertStateAsync(shape, connection, false, 0, 1, 0);
        await SubmitAndCommitAsync(shape, connection);
        await AssertStateAsync(shape, connection, true, 40, 2, 1);
    }

    [Theory]
    [InlineData("direct", "producer")]
    [InlineData("direct", "allowance")]
    [InlineData("direct", "receipt")]
    [InlineData("direct", "allocation")]
    [InlineData("direct", "commit")]
    [InlineData("mapped", "producer")]
    [InlineData("mapped", "allowance")]
    [InlineData("mapped", "receipt")]
    [InlineData("mapped", "allocation")]
    [InlineData("mapped", "commit")]
    public async Task AnyRequiredWriteOrCallerCommitFailureRollsBackTheCompleteCascade(
        string shape,
        string fault
    )
    {
        string connection = await SetupAsync(shape);
        string schema = shape == "direct" ? "direct_orders" : "mapped_quotes";
        string producer = shape == "direct" ? "orders" : "quotes";
        string submitted = shape == "direct" ? "Submitted" : "Accepted";
        string table = fault switch
        {
            "producer" => producer,
            "allowance" or "commit" => "allowances",
            "receipt" => "receipts",
            _ => "allocations",
        };
        string check = fault switch
        {
            "producer" => $"NOT \"{submitted}\"",
            "allowance" => "\"Reserved\" = 0",
            "receipt" => $"\"OrderId\" <> '{First:D}'::uuid",
            _ => "\"Amount\" = 0",
        };
        string sql =
            fault == "commit"
                ? $"""
                    CREATE FUNCTION {schema}.fail_commit() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN RAISE EXCEPTION 'assessment commit rejected'; END; $$;
                    CREATE CONSTRAINT TRIGGER assessment_fault AFTER UPDATE ON {schema}.allowances
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {schema}.fail_commit();
                    """
                : $"ALTER TABLE {schema}.{table} ADD CONSTRAINT assessment_fault CHECK ({check})";
        await ExecuteAsync(connection, sql);
        await using (var operation = new Scenario(shape, connection))
        {
            await using var transaction = await operation.Context.Database.BeginTransactionAsync(
                Token
            );
            await operation.SubmitAsync(First, Token);
            if (fault == "commit")
            {
                var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                    SaveAndCommitAsync(operation.Context, transaction)
                );
                Assert.Equal("assessment commit rejected", failure.MessageText);
                Assert.Equal(0, operation.PendingCount);
            }
            else
            {
                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    operation.Context.SaveChangesAsync(Token)
                );
                Assert.Equal(0, operation.PendingCount);
            }

            Assert.Same(transaction, operation.Context.Database.CurrentTransaction);
        }

        await AssertStateAsync(shape, connection, false, 0, 1, 0);
        await ExecuteAsync(
            connection,
            fault == "commit"
                ? $"DROP TRIGGER assessment_fault ON {schema}.allowances; DROP FUNCTION {schema}.fail_commit()"
                : $"ALTER TABLE {schema}.{table} DROP CONSTRAINT assessment_fault"
        );
        await SubmitAndCommitAsync(shape, connection);
        await AssertStateAsync(shape, connection, true, 40, 2, 1);
    }

    [Theory]
    [InlineData("direct", false)]
    [InlineData("direct", true)]
    [InlineData("mapped", false)]
    [InlineData("mapped", true)]
    public async Task CancellationBeforeOrDuringHandlingLeavesNoCommittedPartialEffects(
        string shape,
        bool during
    )
    {
        string connection = await SetupAsync(shape);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var probe = new DuringAllowanceRead(_ => cancellation.Cancel());
        await using var operation = new Scenario(
            shape,
            connection,
            interceptors: during ? [probe] : []
        );
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        if (!during)
            cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            operation.Context.SaveChangesAsync(cancellation.Token)
        );
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task NestedSaveFromHandlingFailsWithoutRecursionOrPartialCommit(string shape)
    {
        string connection = await SetupAsync(shape);
        var probe = new DuringAllowanceRead(context => context.SaveChanges());
        await using var operation = new Scenario(shape, connection, interceptors: [probe]);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.Context.SaveChangesAsync(Token)
        );
        Assert.True(probe.Invoked);
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task TheInvocationCeilingRollsBackAFiniteButOversizedCascade(string shape)
    {
        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(shape, connection, ceiling: 2);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.Context.SaveChangesAsync(Token)
        );
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task HandlerAddedInvalidRelationshipsFailThroughNativeEfConstraints(string shape)
    {
        string connection = await SetupAsync(shape);
        var probe = new DuringAllowanceRead(context =>
        {
            if (context is DirectOrdersContext direct)
                direct.Receipts.Add(new() { Owner = "beta", OrderId = Guid.NewGuid() });
            else
                ((MappedQuotesContext)context).Receipts.Add(
                    new() { Owner = "beta", OrderId = Guid.NewGuid() }
                );
        });
        await using var operation = new Scenario(shape, connection, interceptors: [probe]);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            operation.Context.SaveChangesAsync(Token)
        );
        Assert.Equal("23503", Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task FactsRaisedByPersistenceCallbacksAreOutsideBeforeSaveDispatch(string shape)
    {
        string connection = await SetupAsync(shape);
        var probe = new LateFact();
        await using var operation = new Scenario(shape, connection, interceptors: [probe]);
        probe.RaiseMapped = () => operation.RaiseMapped(new QuoteAccepted(First, 40));
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await operation.Context.SaveChangesAsync(Token);
        Assert.True(probe.Invoked);
        Assert.Equal(1, operation.PendingCount);
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task DirectSqlIsStillOutsideTheSupportedCollectionAndDispatchProtocol(string shape)
    {
        string connection = await SetupAsync(shape);
        string schema = shape == "direct" ? "direct_orders" : "mapped_quotes";
        string table = shape == "direct" ? "orders" : "quotes";
        string property = shape == "direct" ? "Submitted" : "Accepted";
        await ExecuteAsync(
            connection,
            $"UPDATE {schema}.{table} SET \"{property}\" = TRUE, \"Version\" = 2 WHERE \"Id\" = '{First:D}'"
        );
        await using var operation = new Scenario(shape, connection);
        Assert.Equal(0, await operation.Context.SaveChangesAsync(Token));
        await AssertStateAsync(shape, connection, true, 0, 1, 0);
    }

    private static async Task SaveAndCommitAsync(
        DbContext context,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction
    )
    {
        await context.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    private sealed class CompletionRejectedException : Exception;

    private sealed class RejectCompletion : SaveChangesInterceptor
    {
        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) =>
            throw new CompletionRejectedException();

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        ) => throw new CompletionRejectedException();
    }

    private sealed class CancelNativeWrite(CancellationTokenSource cancellation)
        : DbCommandInterceptor
    {
        public bool Invoked { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.Ordinal))
            {
                Invoked = true;
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class DuringAllowanceRead(Action<DbContext> action) : DbCommandInterceptor
    {
        public bool Invoked { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Invoked && command.CommandText.Contains("allowances", StringComparison.Ordinal))
            {
                Invoked = true;
                action(eventData.Context!);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class LateFact : SaveChangesInterceptor
    {
        public Action RaiseMapped { get; set; } = null!;
        public bool Invoked { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Invoked = true;
            if (eventData.Context is DirectOrdersContext direct)
                direct.Orders.Local.Single().Events.Raise(new OrderSubmitted(First, 40));
            else
                RaiseMapped();

            return ValueTask.FromResult(result);
        }
    }
}
