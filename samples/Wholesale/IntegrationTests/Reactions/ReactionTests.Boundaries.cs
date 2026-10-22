using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.DomainEvents;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reactions;

public sealed partial class ReactionTests
{
    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    public async Task DirectTrackedSubmissionDoesNotInventADomainFact(string save)
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var root = await database
            .DraftOrders.Include(row => row.Lines)
            .SingleAsync(row => row.Id == First, Token);
        root.IsSubmitted = true;
        root.Version++;
        await SaveAsync(database, save);
        await transaction.CommitAsync(Token);
        await AssertStateAsync(provider, First, true, 0, 1);
    }

    [Theory]
    [InlineData("bulk")]
    [InlineData("sql")]
    [InlineData("provider")]
    public async Task DirectWritesAreCounterexamplesToUniversalDomainEventCoverage(string path)
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await using (var scope = DomainEventComposition.CreateScope(provider))
        {
            var database = Db(scope);
            if (path == "bulk")
                await database
                    .DraftOrders.Where(row => row.Id == First)
                    .ExecuteUpdateAsync(
                        set =>
                            set.SetProperty(row => row.IsSubmitted, true)
                                .SetProperty(row => row.Version, row => row.Version + 1),
                        Token
                    );
            else if (path == "sql")
                await database.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE sales.draft_orders SET is_submitted = TRUE, version = version + 1 WHERE id = {First}",
                    Token
                );
            else
                await ExecuteAsync(
                    connection,
                    $"UPDATE sales.draft_orders SET is_submitted = TRUE, version = version + 1 WHERE id = '{First:D}'"
                );

            Assert.Empty(database.ChangeTracker.Entries());
            Assert.Equal(0, Events(scope).PendingCount);
            Assert.Equal(0, await database.SaveChangesAsync(Token));
        }

        // These writes deliberately leave a submitted order without its required reservation.
        await AssertStateAsync(provider, First, true, 0, 1);
    }

    [Fact]
    public async Task AnotherOrganizationCannotLoadTheProducerOrReserveItsAllowance()
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await using var scope = DomainEventComposition.CreateScope(provider, "wholesale-beta");
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.NotFound>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        Assert.Null(
            await scope
                .ServiceProvider.GetRequiredService<ICustomerOrderAllowances>()
                .ReadAsync(SalesDemoSeed.AlphaCustomerId, "EUR", Token)
        );
        Assert.Equal(0, await database.SaveChangesAsync(Token));
        await transaction.CommitAsync(Token);
        await AssertStateAsync(provider, First, false, 0, 1);
    }

    [Fact]
    public async Task ForwardMigrationRetainsAnExistingDraftAndDoesNotInventHistoricalFacts()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using (var scope = DomainEventComposition.CreateScope(provider))
        {
            var database = Db(scope);
            await database
                .GetService<IMigrator>()
                .MigrateAsync("20261014171512_AddDraftOrderChildren", Token);
            SalesDemoSeed.Stage(database);
            await database.SaveChangesAsync(Token);
        }

        // Insert the previous schema's valid shape without asking the new runtime to query a missing column.
        await ExecuteAsync(
            connection,
            $"""
            INSERT INTO sales.draft_orders (organization_key,id,customer_id,code,version,currency,total_limit)
            VALUES ('wholesale-alpha','{First:D}','{SalesDemoSeed.AlphaCustomerId:D}','OLD-DRAFT',1,'EUR',100);
            INSERT INTO sales.draft_order_lines (organization_key,order_id,id,sku,quantity,unit_price)
            VALUES ('wholesale-alpha','{First:D}','{DomainEventJourney.LineId:D}','DEMO-NOTEBOOK',1,40);
            """
        );
        await using var migrated = DomainEventComposition.CreateScope(provider);
        var sales = Db(migrated);
        var before = (await sales.Database.GetAppliedMigrationsAsync(Token)).ToArray();
        await sales.Database.MigrateAsync(Token);
        var after = (await sales.Database.GetAppliedMigrationsAsync(Token)).ToArray();
        Assert.Equal(before, after.Take(before.Length));
        Assert.Equal(sales.Database.GetMigrations(), after);
        Assert.False(sales.Database.HasPendingModelChanges());
        // Upgrading storage invents no resource grants for historical rows.
        Assert.Null(
            await migrated
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .ReadAsync(First, Token)
        );
        var stored = await sales
            .DraftOrders.AsNoTracking()
            .Include(row => row.Lines)
            .SingleAsync(row => row.Id == First, Token);
        var draft = stored.ToAggregate().ToView();
        Assert.NotNull(draft);
        Assert.False(draft.IsSubmitted);
        Assert.Equal(1, draft.Version);
        Assert.Equal(40, draft.Total);
        Assert.Equal(0, Events(migrated).PendingCount);
        Assert.Empty(await sales.OrderAllowances.ToArrayAsync(Token));
        Assert.Equal(0, await sales.SaveChangesAsync(Token));
    }
}
