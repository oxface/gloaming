using System.Data.Common;
using Gloaming.EventReactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.DomainEvents;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.OrderAllowances;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reactions;

public sealed partial class ReactionTests
{
    [Fact]
    public async Task DifferentOrdersCompeteOnTheRequiredAllowancesNativeVersion()
    {
        string connection = await SetupAsync(firstAmount: 60, secondAmount: 60);
        await using var provider = Provider(connection);
        await using var first = DomainEventComposition.CreateScope(provider);
        await using var second = DomainEventComposition.CreateScope(provider);
        await using var firstTx = await Db(first).Database.BeginTransactionAsync(Token);
        await using var secondTx = await Db(second).Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(first).SubmitAsync(new(First, 1), Token)
        );
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(second).SubmitAsync(new(Second, 1), Token)
        );
        await Db(first).OrderAllowances.SingleAsync(Token);
        await Db(second).OrderAllowances.SingleAsync(Token);
        await Db(first).SaveChangesAsync(Token);
        await firstTx.CommitAsync(Token);
        var failure = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            Db(second).SaveChangesAsync(Token)
        );
        Assert.IsType<OrderAllowanceRow>(Assert.Single(failure.Entries).Entity);
        Assert.True(DraftOrderFailures.IsVersionConflict(failure));
        Assert.Equal(0, Events(second).PendingCount);
        await secondTx.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(provider, First, true, 60, 2);
        await AssertStateAsync(provider, Second, false, 60, 2);
    }

    [Theory]
    [InlineData("producer")]
    [InlineData("reaction")]
    [InlineData("commit")]
    public async Task RequiredWriteAndCommitFailuresRollBackBothAggregates(string fault)
    {
        string connection = await SetupAsync();
        string sql = fault switch
        {
            "producer" =>
                "ALTER TABLE sales.draft_orders ADD CONSTRAINT d2_fault CHECK (NOT is_submitted)",
            "reaction" =>
                "ALTER TABLE sales.customer_order_allowances ADD CONSTRAINT d2_fault CHECK (reserved = 0)",
            "commit" => """
                CREATE FUNCTION sales.d2_fault() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'submission commit rejected'; END; $$;
                CREATE CONSTRAINT TRIGGER d2_fault AFTER UPDATE ON sales.customer_order_allowances
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sales.d2_fault();
                """,
            _ => throw new InvalidOperationException(),
        };
        await ExecuteAsync(connection, sql);
        await using var provider = Provider(connection);
        await using (var scope = DomainEventComposition.CreateScope(provider))
        {
            var database = Db(scope);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await Commands(scope).SubmitAsync(new(First, 1), Token)
            );
            if (fault == "commit")
            {
                await database.SaveChangesAsync(Token);
                Assert.Equal(0, Events(scope).PendingCount);
                await AssertStateAsync(provider, First, false, 0, 1);
                var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                    transaction.CommitAsync(Token)
                );
                Assert.Equal("submission commit rejected", failure.MessageText);
            }
            else
            {
                var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    database.SaveChangesAsync(Token)
                );
                Assert.False(DraftOrderFailures.IsVersionConflict(failure));
                Assert.Equal(0, Events(scope).PendingCount);
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        await AssertStateAsync(provider, First, false, 0, 1);
        await ExecuteAsync(
            connection,
            fault == "commit"
                ? "DROP TRIGGER d2_fault ON sales.customer_order_allowances; DROP FUNCTION sales.d2_fault()"
                : $"ALTER TABLE sales.{(fault == "producer" ? "draft_orders" : "customer_order_allowances")} DROP CONSTRAINT d2_fault"
        );
        await SubmitAndCommitAsync(provider, First);
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeOrDuringHandlingCannotCommitTheProducer(bool duringHandling)
    {
        string connection = await SetupAsync();
        using var cancellation = new CancellationTokenSource();
        var probe = new DuringAllowanceRead(_ => cancellation.Cancel());
        await using var provider = duringHandling
            ? Provider(connection, probe)
            : Provider(connection);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        if (!duringHandling)
            cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SaveChangesAsync(cancellation.Token)
        );
        Assert.Equal(duringHandling ? 0 : 1, Events(scope).PendingCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        await transaction.RollbackAsync(CancellationToken.None);
        await using var observer = Provider(connection);
        await AssertStateAsync(observer, First, false, 0, 1);
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    public async Task NestedSaveDuringHandlingIsRejectedWithoutRecursion(string save)
    {
        string connection = await SetupAsync();
        var probe = new DuringAllowanceRead(database => database.SaveChanges());
        await using var provider = Provider(connection, probe);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(database, save));
        Assert.True(probe.Invoked);
        await transaction.RollbackAsync(CancellationToken.None);
        await using var observer = Provider(connection);
        await AssertStateAsync(observer, First, false, 0, 1);
    }

    [Fact]
    public async Task FactsProducedDuringHandlingAreDrainedByTheRegisteredDispatcher()
    {
        string connection = await SetupAsync(secondAmount: 30);
        IEventSink<SalesModule>? sink = null;
        var probe = new DuringAllowanceRead(database =>
        {
            var root = database.DraftOrders.Local.Single(row => row.Id == Second);
            var (candidate, fact) = root.ToAggregate().Submit();
            root.Version = candidate.Version;
            root.IsSubmitted = candidate.IsSubmitted;
            sink!.Raise(fact);
        });
        await using var provider = Provider(connection, probe);
        await using var scope = DomainEventComposition.CreateScope(provider);
        sink = scope.ServiceProvider.GetRequiredService<IEventSink<SalesModule>>();
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await database
            .DraftOrders.Include(row => row.Lines)
            .SingleAsync(row => row.Id == Second, Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        await database.SaveChangesAsync(Token);
        Assert.True(probe.Invoked);
        Assert.Equal(0, Events(scope).PendingCount);
        await transaction.CommitAsync(Token);
        await AssertStateAsync(provider, First, true, 70, 3);
        await AssertStateAsync(provider, Second, true, 70, 3);
    }

    [Fact]
    public async Task WorkAddedDuringHandlingStillPassesTheExistingTenantGuard()
    {
        string connection = await SetupAsync();
        var probe = new DuringAllowanceRead(database =>
            database.Add(
                new AddressRow
                {
                    Id = Guid.NewGuid(),
                    CustomerId = SalesDemoSeed.AlphaCustomerId,
                    OrganizationKey = "wholesale-beta",
                    AddressLine = "invalid handler work",
                }
            )
        );
        await using var provider = Provider(connection, probe);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        await Assert.ThrowsAsync<Gloaming.Persistence.EntityFrameworkCore.TenantOwnershipException>(
            () =>
                database.SaveChangesAsync(Token)
        );
        Assert.True(probe.Invoked);
        await transaction.RollbackAsync(CancellationToken.None);
        await using var observer = Provider(connection);
        await AssertStateAsync(observer, First, false, 0, 1);
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    // Inject faults at the actual required-handler read, before tracked-save validation.
    private sealed class DuringAllowanceRead(Action<SalesDbContext> action) : DbCommandInterceptor
    {
        public bool Invoked { get; private set; }

        private void Invoke(DbCommand command, CommandEventData eventData)
        {
            if (
                !Invoked
                && command.CommandText.Contains(
                    "customer_order_allowances",
                    StringComparison.Ordinal
                )
            )
            {
                Invoked = true;
                action((SalesDbContext)eventData.Context!);
            }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Invoke(command, eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Invoke(command, eventData);
            return ValueTask.FromResult(result);
        }
    }
}
