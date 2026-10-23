using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.RichAggregate;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.DraftOrders;

public sealed partial class OrderTests
{
    [Fact]
    public async Task DifferentChildDecisionsStillCompeteOnOneRootVersion()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var first = DraftOrderComposition.CreateScope(provider);
        await using var second = DraftOrderComposition.CreateScope(provider);
        var firstDb = first.ServiceProvider.GetRequiredService<SalesDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var firstTx = await firstDb.Database.BeginTransactionAsync(Token);
        await using var secondTx = await secondDb.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            90,
            Assert
                .IsType<DraftOrderChangeResult.Changed>(
                    await ChangeAsync(first, 1, [new DraftOrderLineChange.Change(First, 7, 10)])
                )
                .Proposed.Total
        );
        Assert.Equal(
            80,
            Assert
                .IsType<DraftOrderChangeResult.Changed>(
                    await ChangeAsync(second, 1, [new DraftOrderLineChange.Change(Second, 3, 20)])
                )
                .Proposed.Total
        );
        await firstDb.SaveChangesAsync(Token);
        await firstTx.CommitAsync(Token);
        var failure = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            secondDb.SaveChangesAsync(Token)
        );
        Assert.True(DraftOrderFailures.IsVersionConflict(failure));
        await secondTx.RollbackAsync(CancellationToken.None);
        await AssertOrderAsync(
            provider,
            2,
            90,
            new(First, "DEMO-NOTEBOOK", 7, 10),
            new(Second, "DEMO-NOTEBOOK", 1, 20)
        );
        await using var stale = DraftOrderComposition.CreateScope(provider);
        var database = stale.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Conflict>(
            await ChangeAsync(stale, 1, [new DraftOrderLineChange.Change(Second, 3, 20)])
        );
        Assert.False(database.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("root")]
    [InlineData("child")]
    [InlineData("commit")]
    public async Task RequiredWriteOrCommitFailureRollsBackRootAndAllChildren(string fault)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        string sql = fault switch
        {
            "root" => "ALTER TABLE sales.draft_orders ADD CONSTRAINT d1_fault CHECK (version <= 1)",
            "child" =>
                "ALTER TABLE sales.draft_order_lines ADD CONSTRAINT d1_fault CHECK (quantity <> 3)",
            "commit" => """
                CREATE FUNCTION sales.d1_fault() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'draft commit rejected'; END; $$;
                CREATE CONSTRAINT TRIGGER d1_fault AFTER UPDATE ON sales.draft_orders
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sales.d1_fault();
                """,
            _ => throw new InvalidOperationException(),
        };
        await ExecuteAsync(connection, sql);
        Guid added = Guid.NewGuid();
        await using (var write = DraftOrderComposition.CreateScope(provider))
        {
            var database = write.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await ChangeAsync(
                    write,
                    1,
                    [
                        new DraftOrderLineChange.Change(First, 3, 10),
                        new DraftOrderLineChange.Remove(Second),
                        new DraftOrderLineChange.Add(new(added, "DEMO-NOTEBOOK", 1, 5)),
                    ]
                )
            );
            if (fault == "commit")
            {
                await database.SaveChangesAsync(Token);
                await AssertOriginalAsync(provider);
                var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                    transaction.CommitAsync(Token)
                );
                Assert.Equal("draft commit rejected", failure.MessageText);
            }
            else
            {
                var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    database.SaveChangesAsync(Token)
                );
                Assert.False(DraftOrderFailures.IsVersionConflict(failure));
            }

            // PostgreSQL already rolled back a rejected deferred-constraint COMMIT.
            if (fault != "commit")
                await transaction.RollbackAsync(CancellationToken.None);
        }

        await AssertOriginalAsync(provider);
        await ExecuteAsync(
            connection,
            fault == "commit"
                ? "DROP TRIGGER d1_fault ON sales.draft_orders; DROP FUNCTION sales.d1_fault()"
                : $"ALTER TABLE sales.{(fault == "root" ? "draft_orders" : "draft_order_lines")} DROP CONSTRAINT d1_fault"
        );
        await using var recovered = DraftOrderComposition.CreateScope(provider);
        var recoveredDb = recovered.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var recoveredTx = await recoveredDb.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await ChangeAsync(recovered, 1, [new DraftOrderLineChange.Change(First, 3, 10)])
        );
        await recoveredDb.SaveChangesAsync(Token);
        await recoveredTx.CommitAsync(Token);
        await AssertOrderAsync(
            provider,
            2,
            50,
            new(First, "DEMO-NOTEBOOK", 3, 10),
            new(Second, "DEMO-NOTEBOOK", 1, 20)
        );
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    [InlineData("sync-false")]
    [InlineData("async-false")]
    public async Task DirectTrackedChildWritesDoNotAutomaticallyAdvanceTheAggregateVersion(
        string save
    )
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var write = DraftOrderComposition.CreateScope(provider);
        var database = write.ServiceProvider.GetRequiredService<SalesDbContext>();
        var child = await database.DraftOrderLines.SingleAsync(
            row => row.OrderId == Id && row.Id == First,
            Token
        );
        child.Quantity = 3;
        if (save == "sync")
            database.SaveChanges();
        else if (save == "sync-false")
            database.SaveChanges(false);
        else if (save == "async")
            await database.SaveChangesAsync(Token);
        else
            await database.SaveChangesAsync(false, Token);

        await using var read = DraftOrderComposition.CreateScope(provider);
        var stored = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadAsync(Id, Token);
        Assert.NotNull(stored);
        Assert.Equal(1, stored.Version);
        Assert.Equal(3, stored.Lines.Single(line => line.Id == First).Quantity);
    }

    [Fact]
    public async Task BulkSqlIsAnExplicitCounterexampleToUniversalSaveGuardCoverage()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var bypass = DraftOrderComposition.CreateScope(provider))
        {
            var database = bypass.ServiceProvider.GetRequiredService<SalesDbContext>();
            Assert.Equal(
                1,
                await database
                    .DraftOrderLines.Where(row => row.OrderId == Id && row.Id == First)
                    .ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.Quantity, 20),
                        Token
                    )
            );
            Assert.Equal(
                1,
                (
                    await database
                        .DraftOrders.AsNoTracking()
                        .SingleAsync(row => row.Id == Id, Token)
                ).Version
            );
            Assert.Empty(database.ChangeTracker.Entries());
            await Assert.ThrowsAsync<Sales.DraftOrders.OrderLimitExceededException>(() =>
                bypass.ServiceProvider.GetRequiredService<IDraftOrderQueries>().ReadAsync(Id, Token)
            );
            await database
                .DraftOrderLines.Where(row => row.OrderId == Id && row.Id == First)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Quantity, 2), Token);
        }

        await AssertOriginalAsync(provider);
    }

    [Theory]
    [InlineData("child")]
    [InlineData("customer")]
    public async Task NativeCompositeForeignKeysRejectCrossTenantReferences(string reference)
    {
        string connection = await SeedAsync();
        string sql =
            reference == "child"
                ? $"INSERT INTO sales.draft_order_lines (organization_key, order_id, id, sku, quantity, unit_price) VALUES ('wholesale-beta', '{Id:D}', '{Guid.NewGuid():D}', 'DEMO-NOTEBOOK', 1, 1)"
                : $"INSERT INTO sales.draft_orders (organization_key, id, customer_id, code, version, currency, total_limit) VALUES ('wholesale-beta', '{Guid.NewGuid():D}', '{SalesDemoSeed.AlphaCustomerId:D}', 'CROSS-TENANT', 1, 'EUR', 100)";
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, sql)
        );
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
        await using var provider = Provider(connection);
        await AssertOriginalAsync(provider);
        await using var beta = DraftOrderComposition.CreateScope(provider, "wholesale-beta");
        Assert.Null(
            await beta.ServiceProvider.GetRequiredService<IDraftOrderQueries>().ReadAsync(Id, Token)
        );
    }

    [Fact]
    public async Task ForwardMigrationPreservesExistingCustomerStateAndNativeHistory()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using var scope = DraftOrderComposition.CreateScope(provider);
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await database
            .GetService<IMigrator>()
            .MigrateAsync("20261010164619_AddCompensatingReservationOrders", Token);
        // Write the literal pre-B2 schema; the current EF model includes later delivery columns.
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.customer_profiles (id, organization_key, code, display_name, version)
            VALUES ('10000000-0000-0000-0000-000000000001', 'wholesale-alpha', 'DEMO-CUSTOMER', 'Alpha Customer', 1);
            INSERT INTO sales.customer_addresses (id, organization_key, customer_id, address_line)
            VALUES ('20000000-0000-0000-0000-000000000001', 'wholesale-alpha', '10000000-0000-0000-0000-000000000001', 'Alpha Street');
            """,
            Token
        );
        string[] before = (await database.Database.GetAppliedMigrationsAsync(Token)).ToArray();
        await database.Database.MigrateAsync(Token);
        Assert.False(database.Database.HasPendingModelChanges());
        var after = (await database.Database.GetAppliedMigrationsAsync(Token)).ToArray();
        Assert.Equal(before, after.Take(before.Length));
        Assert.Equal(database.Database.GetMigrations(), after);
        Assert.Empty(await database.DraftOrders.ToArrayAsync(Token));
        var customer = await scope
            .ServiceProvider.GetRequiredService<ICustomerProfiles>()
            .ReadAsync(SalesDemoSeed.AlphaCustomerId, Token);
        Assert.NotNull(customer);
        Assert.Equal("Alpha Customer", customer.DisplayName);
        Assert.Equal("Alpha Street", customer.AddressLine);
        Assert.Equal(1, customer.Version);
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }
}
