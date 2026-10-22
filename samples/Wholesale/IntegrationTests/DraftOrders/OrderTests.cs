using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.RichAggregate;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.DraftOrders;

public sealed partial class OrderTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Guid Id => DraftOrderJourney.OrderId;
    private static Guid First => DraftOrderJourney.FirstLineId;
    private static Guid Second => DraftOrderJourney.SecondLineId;

    [Fact]
    public async Task ExecutableCreatesEditsAndJoinsRelationalStateWithoutMutatingReferences()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        await DraftOrderJourney.RunAsync(connection, output, Token);
        Assert.Contains("rejected total=105.00", output.ToString(), StringComparison.Ordinal);
        await using var provider = Provider(connection);
        await AssertOrderAsync(
            provider,
            2,
            45,
            new(First, "DEMO-NOTEBOOK", 3, 10),
            new(DraftOrderJourney.NewLineId, "DEMO-NOTEBOOK", 1, 15)
        );
        await using var read = DraftOrderComposition.CreateScope(provider);
        var summary = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadSummaryAsync(Id, Token);
        Assert.NotNull(summary);
        Assert.Equal("Current customer name", summary.CustomerName);
        Assert.Equal(45, summary.Total);
        Assert.Equal(2, summary.Version);
        Assert.Equal(
            42,
            (
                await read
                    .ServiceProvider.GetRequiredService<Inventory.Contracts.IStockCatalog>()
                    .ReadAsync("DEMO-NOTEBOOK", Token)
            )!.AvailableQuantity
        );
    }

    [Fact]
    public async Task ProposedGraphRemainsInvisibleThroughSaveUntilTheCallerCommits()
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await using var write = DraftOrderComposition.CreateScope(provider);
        var database = write.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var result = Assert.IsType<DraftOrderChangeResult.Changed>(await CreateAsync(write));
        Assert.Equal(1, result.Proposed.Version);
        Assert.Equal(40, result.Proposed.Total);
        await using (var observer = DraftOrderComposition.CreateScope(provider))
            Assert.Null(
                await observer
                    .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                    .ReadAsync(Id, Token)
            );

        await database.SaveChangesAsync(Token);
        await using (var observer = DraftOrderComposition.CreateScope(provider))
            Assert.Null(
                await observer
                    .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                    .ReadAsync(Id, Token)
            );

        await transaction.CommitAsync(Token);
        await AssertOriginalAsync(provider);
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("invalid-later-child")]
    [InlineData("unknown-sku")]
    [InlineData("empty")]
    public async Task RejectedBatchLeavesTheLoadedGraphAndStorageUntouched(string fault)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var scope = DraftOrderComposition.CreateScope(provider);
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        DraftOrderLineChange[] changes = fault switch
        {
            "limit" =>
            [
                new DraftOrderLineChange.Change(First, 3, 10),
                new DraftOrderLineChange.Change(Second, 4, 20),
            ],
            "invalid-later-child" =>
            [
                new DraftOrderLineChange.Change(First, 3, 10),
                new DraftOrderLineChange.Change(Second, 0, 20),
            ],
            "unknown-sku" =>
            [
                new DraftOrderLineChange.Change(First, 3, 10),
                new DraftOrderLineChange.Add(new(Guid.NewGuid(), "UNKNOWN", 1, 5)),
            ],
            "empty" =>
            [
                new DraftOrderLineChange.Remove(First),
                new DraftOrderLineChange.Remove(Second),
            ],
            _ => throw new InvalidOperationException(),
        };
        if (fault is "invalid-later-child" or "empty")
            await Assert.ThrowsAnyAsync<ArgumentException>(() => ChangeAsync(scope, 1, changes));
        else if (fault == "limit")
            Assert.IsType<DraftOrderChangeResult.LimitExceeded>(
                await ChangeAsync(scope, 1, changes)
            );
        else
            Assert.Equal(
                new DraftOrderChangeResult.UnknownSku("UNKNOWN"),
                await ChangeAsync(scope, 1, changes)
            );

        Assert.False(database.ChangeTracker.HasChanges());
        var loaded = Assert.Single(database.ChangeTracker.Entries<DraftOrderRow>()).Entity;
        Assert.Equal(1, loaded.Version);
        Assert.Equal(2, loaded.Lines.Single(line => line.Id == First).Quantity);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await AssertOriginalAsync(provider);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("sku")]
    public async Task MissingReferencesCannotStageCreation(string missing)
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await using var scope = DraftOrderComposition.CreateScope(provider);
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var request = Request() with
        {
            CustomerId =
                missing == "customer"
                    ? SalesDemoSeed.BetaCustomerId
                    : SalesDemoSeed.AlphaCustomerId,
            Lines = missing == "sku" ? [new(First, "UNKNOWN", 1, 10)] : Request().Lines,
        };
        var result = await scope
            .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
            .CreateAsync(request, Token);
        if (missing == "customer")
            Assert.IsType<DraftOrderChangeResult.CustomerNotFound>(result);
        else
            Assert.Equal(new DraftOrderChangeResult.UnknownSku("UNKNOWN"), result);

        Assert.Empty(database.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulChildWritesUseBothNativeSaveOverloadFamilies(bool synchronous)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var scope = DraftOrderComposition.CreateScope(provider);
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await ChangeAsync(scope, 1, [new DraftOrderLineChange.Change(First, 3, 10)])
        );
        if (synchronous)
            database.SaveChanges(acceptAllChangesOnSuccess: false);
        else
            await database.SaveChangesAsync(acceptAllChangesOnSuccess: false, Token);

        database.ChangeTracker.AcceptAllChanges();
        await transaction.CommitAsync(Token);
        await AssertOrderAsync(
            provider,
            2,
            50,
            new(First, "DEMO-NOTEBOOK", 3, 10),
            new(Second, "DEMO-NOTEBOOK", 1, 20)
        );
    }

    [Fact]
    public async Task RemovingOneOrdersChildPreservesSameLineIdentityInAnotherOrder()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        Guid other = Guid.NewGuid();
        await using (var create = DraftOrderComposition.CreateScope(provider))
        {
            var database = create.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<DraftOrderChangeResult.Changed>(await CreateAsync(create, other));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using (var remove = DraftOrderComposition.CreateScope(provider))
        {
            var database = remove.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await ChangeAsync(remove, 1, [new DraftOrderLineChange.Remove(Second)])
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await AssertOrderAsync(provider, 2, 20, new DraftOrderLine(First, "DEMO-NOTEBOOK", 2, 10));
        await using var read = DraftOrderComposition.CreateScope(provider);
        var otherOrder = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadAsync(other, Token);
        Assert.NotNull(otherOrder);
        Assert.Equal(1, otherOrder.Version);
        Assert.Equal(40, otherOrder.Total);
        Assert.Equal(2, otherOrder.Lines.Count);
        Assert.Contains(otherOrder.Lines, line => line.Id == Second);
        Assert.Equal(
            3,
            await read
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .DraftOrderLines.CountAsync(Token)
        );
    }

    [Theory]
    [InlineData("transaction")]
    [InlineData("actor")]
    public async Task WriteAdmissionRejectsMissingTransactionOrHumanBeforeTracking(string missing)
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await using var scope =
            missing == "actor"
                ? provider.CreateAsyncScope()
                : DraftOrderComposition.CreateScope(provider);
        if (missing == "actor")
        {
            scope
                .ServiceProvider.GetRequiredService<Gloaming.Tenancy.ITenantContextInitializer>()
                .Initialize(
                    Gloaming.Tenancy.TenantContext.ForTenant(
                        new Gloaming.Tenancy.TenantId("wholesale-alpha")
                    )
                );
            scope
                .ServiceProvider.GetRequiredService<Gloaming.ActorIdentity.IActorContextInitializer>()
                .Initialize(new(Gloaming.ActorIdentity.Actor.System(new("system"))));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(scope));
        Assert.Empty(
            scope.ServiceProvider.GetRequiredService<SalesDbContext>().ChangeTracker.Entries()
        );
    }

    private static CreateDraftOrder Request(Guid? id = null) =>
        new(
            id ?? Id,
            SalesDemoSeed.AlphaCustomerId,
            (id ?? Id).ToString("N"),
            "EUR",
            100,
            [new(First, "DEMO-NOTEBOOK", 2, 10), new(Second, "DEMO-NOTEBOOK", 1, 20)]
        );

    private static Task<DraftOrderChangeResult> CreateAsync(
        AsyncServiceScope scope,
        Guid? id = null
    ) =>
        scope
            .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
            .CreateAsync(Request(id), Token);

    private static Task<DraftOrderChangeResult> ChangeAsync(
        AsyncServiceScope scope,
        long version,
        IReadOnlyList<DraftOrderLineChange> changes
    ) =>
        scope
            .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
            .ChangeLinesAsync(new(Id, version, changes), Token);

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await DraftOrderComposition.SetupAsync(provider, Token);
        await using var beta = DraftOrderComposition.CreateScope(provider, "wholesale-beta");
        var sales = beta.ServiceProvider.GetRequiredService<SalesDbContext>();
        SalesDemoSeed.Stage(sales);
        await sales.SaveChangesAsync(Token);
        return connection;
    }

    private async Task<string> SeedAsync()
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await using var scope = DraftOrderComposition.CreateScope(provider);
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(await CreateAsync(scope));
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return connection;
    }

    private static ServiceProvider Provider(string connection) =>
        DraftOrderComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

    private static Task AssertOriginalAsync(ServiceProvider provider) =>
        AssertOrderAsync(
            provider,
            1,
            40,
            new(First, "DEMO-NOTEBOOK", 2, 10),
            new(Second, "DEMO-NOTEBOOK", 1, 20)
        );

    private static async Task AssertOrderAsync(
        ServiceProvider provider,
        long version,
        decimal total,
        params DraftOrderLine[] lines
    )
    {
        await using var read = DraftOrderComposition.CreateScope(provider);
        var order = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadAsync(Id, Token);
        Assert.NotNull(order);
        Assert.Equal(version, order.Version);
        Assert.Equal(total, order.Total);
        Assert.Equal(100, order.TotalLimit);
        Assert.Equal("EUR", order.Currency);
        Assert.Equal(SalesDemoSeed.AlphaCustomerId, order.CustomerId);
        Assert.Equal(lines.OrderBy(line => line.Id), order.Lines.OrderBy(line => line.Id));
    }
}
