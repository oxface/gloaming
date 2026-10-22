using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Queries;

public sealed partial class QueryTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Guid OtherCustomer => Guid.Parse("c1000000-0000-0000-0000-000000000200");

    private static Guid Id(int suffix) =>
        Guid.Parse($"c1000000-0000-0000-0000-{suffix:000000000000}");

    private static SearchDraftOrders Filter => new("Alpha", "EUR", 30, Limit: 1);

    [Fact]
    public async Task ExecutableUsesTypedContractsAndQueriesCommittedState()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        await CqrsJourney.RunAsync(connection, output, Token);
        Assert.Contains(
            "count=2, page=1, first total=45",
            output.ToString(),
            StringComparison.Ordinal
        );
        await using var provider = Provider(connection);
        await using var read = CqrsComposition.CreateScope(provider);
        var order = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadAsync(CqrsDemoData.OrderId, Token);
        Assert.NotNull(order);
        Assert.Equal(2, order.Version);
        Assert.Equal(45, order.Total);
        Assert.Equal(3, Assert.Single(order.Lines).Quantity);
        Assert.Equal(
            3,
            Assert
                .Single(
                    await read
                        .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                        .ReadAvailableAsync(3, Token)
                )
                .Available
        );
    }

    [Fact]
    public async Task JoinedFiltersPrecedeCountAndDeterministicPaginationWithoutTracking()
    {
        string connection = await SeedAsync();
        var sql = new QueryProbe();
        await using var provider = Provider(connection, sql);
        await using var read = CqrsComposition.CreateScope(provider);
        var queries = read.ServiceProvider.GetRequiredService<IDraftOrderQueries>();
        var first = await queries.SearchAsync(Filter, Token);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(Id(20), Assert.Single(first.Items).Id);
        Assert.Equal(40, first.Items[0].Total);
        Assert.Equal(2, first.Items[0].LineCount);
        Assert.Equal("Alpha Customer", first.Items[0].CustomerName);
        Assert.Equal(2, sql.Commands.Count);
        Assert.All(
            sql.Commands,
            command => Assert.Contains("customer_profiles", command, StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            sql.Commands,
            command => command.Contains("inventory.", StringComparison.Ordinal)
        );

        var second = await queries.SearchAsync(Filter with { Offset = 1 }, Token);
        var third = await queries.SearchAsync(Filter with { Offset = 2 }, Token);
        var beyond = await queries.SearchAsync(Filter with { Offset = 3 }, Token);
        Assert.Equal(Id(30), Assert.Single(second.Items).Id);
        Assert.Equal(Id(40), Assert.Single(third.Items).Id);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(3, third.TotalCount);
        Assert.Equal(3, beyond.TotalCount);
        Assert.Empty(beyond.Items);
        Assert.Equal(first.Items, (await queries.SearchAsync(Filter, Token)).Items);

        var all = await queries.SearchAsync(new(), Token);
        Assert.Equal(6, all.TotalCount);
        Assert.Equal(
            new[] { Id(10), Id(20), Id(30), Id(60), Id(40), Id(50) },
            all.Items.Select(item => item.Id)
        );
        var missing = await queries.SearchAsync(new(CustomerNameContains: "Absent"), Token);
        Assert.Equal(0, missing.TotalCount);
        Assert.Empty(missing.Items);
        Assert.Equal(first.Items[0], await queries.ReadSummaryAsync(Id(20), Token));
        Assert.Empty(
            read.ServiceProvider.GetRequiredService<SalesDbContext>().ChangeTracker.Entries()
        );
    }

    [Fact]
    public async Task NameFilterUsesCurrentJoinedCustomerAndLiteralCaseSensitiveSubstring()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var change = CqrsComposition.CreateScope(provider))
        {
            Assert.IsType<ProfileChangeResult.Updated>(
                await change
                    .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                    .ChangeAsync(
                        new(
                            SalesDemoSeed.AlphaCustomerId,
                            SalesDemoSeed.AlphaAddressId,
                            1,
                            "Current %_ Customer",
                            "Alpha Street"
                        ),
                        Token
                    )
            );
        }

        await using var read = CqrsComposition.CreateScope(provider);
        var queries = read.ServiceProvider.GetRequiredService<IDraftOrderQueries>();
        Assert.Empty((await queries.SearchAsync(Filter, Token)).Items);
        var page = await queries.SearchAsync(
            Filter with
            {
                CustomerNameContains = "%_",
                Limit = 100,
            },
            Token
        );
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { Id(20), Id(30), Id(40) }, page.Items.Select(item => item.Id));
        Assert.All(page.Items, row => Assert.Equal("Current %_ Customer", row.CustomerName));
        Assert.Equal(1, page.Items[0].Version);
        Assert.Equal(40, page.Items[0].Total);
        Assert.Empty(
            (await queries.SearchAsync(new(CustomerNameContains: "current"), Token)).Items
        );
        Assert.Empty((await queries.SearchAsync(new(Currency: "eur"), Token)).Items);
    }

    [Fact]
    public async Task SameDraftIdentityAndCodeInAnotherTenantRemainIsolated()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var beta = CqrsComposition.CreateScope(provider, "wholesale-beta");
        var queries = beta.ServiceProvider.GetRequiredService<IDraftOrderQueries>();
        var page = await queries.SearchAsync(new(MinimumTotal: 30), Token);
        var row = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(Id(20), row.Id);
        Assert.Equal(90, row.Total);
        Assert.Equal("Beta Customer", row.CustomerName);
        Assert.Equal(SalesDemoSeed.BetaCustomerId, row.CustomerId);
        Assert.Empty((await queries.SearchAsync(Filter, Token)).Items);
        Assert.Null(await queries.ReadSummaryAsync(Id(30), Token));
    }

    [Fact]
    public async Task InventoryFiltersReservedAvailabilityInSqlWithoutReplayingHistory()
    {
        string connection = await SeedAsync();
        var sql = new QueryProbe();
        await using var provider = Provider(connection, sql);
        await using var alpha = CqrsComposition.CreateScope(provider);
        var queries = alpha.ServiceProvider.GetRequiredService<IStockPositionQueries>();
        Assert.Equal(3, Assert.Single(await queries.ReadAvailableAsync(3, Token)).Available);
        Assert.Empty(await queries.ReadAvailableAsync(4, Token));
        Assert.All(
            sql.Commands,
            command => Assert.Contains("JOIN", command, StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            sql.Commands,
            command => command.Contains("stored_events", StringComparison.Ordinal)
        );
        Assert.Empty(
            alpha.ServiceProvider.GetRequiredService<InventoryDbContext>().ChangeTracker.Entries()
        );
        await using var beta = CqrsComposition.CreateScope(provider, "wholesale-beta");
        Assert.Empty(
            await beta
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadAvailableAsync(1, Token)
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queries.ReadAvailableAsync(0, Token)
        );
    }

    private async Task<string> SeedAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await CqrsComposition.SetupAsync(provider, Token);
        await using (var setup = CqrsComposition.CreateScope(provider))
        {
            var database = setup.ServiceProvider.GetRequiredService<SalesDbContext>();
            database.Add(
                new CustomerRow
                {
                    Id = OtherCustomer,
                    OrganizationKey = "wholesale-alpha",
                    Code = "OTHER",
                    DisplayName = "Other Customer",
                    Version = 1,
                }
            );
            await database.SaveChangesAsync(Token);
        }

        foreach (
            var (suffix, total, currency) in new[]
            {
                (10, 5m, "EUR"),
                (20, 40m, "EUR"),
                (30, 40m, "EUR"),
                (40, 60m, "EUR"),
                (50, 80m, "USD"),
                (60, 50m, "EUR"),
            }
        )
        {
            var request = CqrsDemoData.Draft(Id(suffix), total, currency);
            if (suffix == 20)
                request = request with
                {
                    Lines =
                    [
                        new(Id(1), "DEMO-NOTEBOOK", 2, 10),
                        new(Id(2), "DEMO-NOTEBOOK", 1, 20),
                    ],
                };

            if (suffix == 60)
                request = request with { CustomerId = OtherCustomer };

            await CreateAsync(provider, request);
        }

        await using (var beta = CqrsComposition.CreateScope(provider, "wholesale-beta"))
        {
            var sales = beta.ServiceProvider.GetRequiredService<SalesDbContext>();
            SalesDemoSeed.Stage(sales);
            await sales.SaveChangesAsync(Token);
            var inventory = beta.ServiceProvider.GetRequiredService<InventoryDbContext>();
            InventoryDemoSeed.Stage(inventory, 90);
            await inventory.SaveChangesAsync(Token);
        }

        await CreateAsync(
            provider,
            CqrsDemoData.Draft(Id(20), 90) with
            {
                CustomerId = SalesDemoSeed.BetaCustomerId,
            },
            "wholesale-beta"
        );
        return connection;
    }

    private static async Task CreateAsync(
        ServiceProvider provider,
        CreateDraftOrder request,
        string owner = "wholesale-alpha"
    )
    {
        await using var write = CqrsComposition.CreateScope(provider, owner);
        var database = write.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await write
                .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                .CreateAsync(request, Token)
        );
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static ServiceProvider Provider(string connection, IInterceptor? probe = null)
    {
        var services = CqrsComposition.CreateServices(connection);
        if (probe is not null)
        {
            services.AddDbContext<SalesDbContext>(options => options.AddInterceptors(probe));
            services.AddDbContext<InventoryDbContext>(options => options.AddInterceptors(probe));
        }

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }
}
