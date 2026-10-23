using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.DomainEvents;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reactions;

public sealed partial class ReactionTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Guid First => DomainEventJourney.FirstOrderId;
    private static Guid Second => DomainEventJourney.SecondOrderId;

    private static SalesDbContext Db(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<SalesDbContext>();

    private static EventReactionDispatcher<SalesModule> Events(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<EventReactionDispatcher<SalesModule>>();

    private static IDraftOrderCommands Commands(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IDraftOrderCommands>();

    [Fact]
    public async Task ExecutableDemonstratesRegisteredRequiredHandling()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        using var output = new StringWriter();
        await DomainEventJourney.RunAsync(connection, output, Token);
        Assert.Contains("reserved=90.00", output.ToString(), StringComparison.Ordinal);
        await using var provider = Provider(connection);
        await AssertStateAsync(provider, First, true, 90, 3);
        await AssertStateAsync(provider, Second, true, 90, 3, orderVersion: 3);
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("sync-false")]
    [InlineData("async")]
    [InlineData("async-false")]
    public async Task AllSaveOverloadsCommitSubmissionAndReactionTogetherWithoutRepeating(
        string save
    )
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        Assert.Equal(1, Events(scope).PendingCount);
        await SaveAsync(database, save);
        Assert.Equal(0, Events(scope).PendingCount);
        await AssertStateAsync(provider, First, false, 0, 1);
        if (save.EndsWith("false", StringComparison.Ordinal))
            database.ChangeTracker.AcceptAllChanges();

        Assert.Equal(0, await database.SaveChangesAsync(Token));
        await transaction.CommitAsync(Token);
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FalseAcceptanceUsesNativeConcurrencyOnAnIncorrectRepeatedSave(
        bool synchronous
    )
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        if (synchronous)
            database.SaveChanges(false);
        else
            await database.SaveChangesAsync(false, Token);

        Assert.Equal(0, Events(scope).PendingCount);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            database.SaveChangesAsync(Token)
        );
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(provider, First, false, 0, 1);
    }

    [Fact]
    public async Task RepeatedSavingAndLaterFactsUseTheSameTransaction()
    {
        string connection = await SetupAsync(secondAmount: 30);
        await using var provider = Provider(connection);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        Assert.Equal(1, Events(scope).PendingCount);
        await database.SaveChangesAsync(false, Token);
        database.ChangeTracker.AcceptAllChanges();
        Assert.Equal(0, await database.SaveChangesAsync(Token));
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(Second, 1), Token)
        );
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await AssertStateAsync(provider, First, true, 70, 3);
        await AssertStateAsync(provider, Second, true, 70, 3);
    }

    [Fact]
    public async Task LaterRequiredRejectionDiscardsTheWholeBatchAndRequiresFreshRecovery()
    {
        string connection = await SetupAsync(secondAmount: 70);
        await using var provider = Provider(connection);
        await using (var scope = DomainEventComposition.CreateScope(provider))
        {
            var database = Db(scope);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await Commands(scope).SubmitAsync(new(First, 1), Token)
            );
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await Commands(scope).SubmitAsync(new(Second, 1), Token)
            );
            var failure = await Assert.ThrowsAsync<OrderAllowanceExceededException>(() =>
                database.SaveChangesAsync(Token)
            );
            Assert.Equal(110, failure.Requested);
            Assert.Equal(0, Events(scope).PendingCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await AssertStateAsync(provider, First, false, 0, 1);
        await AssertStateAsync(provider, Second, false, 0, 1);
        await SubmitAndCommitAsync(provider, First);
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Fact]
    public async Task ReadsHydrationStaleRequestsAndAlreadySubmittedDecisionsProduceNoFacts()
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await SubmitAndCommitAsync(provider, First);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Conflict>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        Assert.IsType<DraftOrderChangeResult.AlreadySubmitted>(
            await Commands(scope).SubmitAsync(new(First, 2), Token)
        );
        Assert.IsType<DraftOrderChangeResult.NotFound>(
            await Commands(scope).SubmitAsync(new(Guid.NewGuid(), 1), Token)
        );
        Assert.IsType<DraftOrderChangeResult.AlreadySubmitted>(
            await Commands(scope)
                .ChangeLinesAsync(
                    new(
                        First,
                        2,
                        [new DraftOrderLineChange.Change(DomainEventJourney.LineId, 1, 20)]
                    ),
                    Token
                )
        );
        var loaded = Assert
            .Single(database.ChangeTracker.Entries<Sales.DraftOrders.DraftOrderRow>())
            .Entity.ToAggregate();
        Assert.Equal(0, Events(scope).PendingCount);
        Assert.Equal(0, await database.SaveChangesAsync(Token));
        await transaction.CommitAsync(Token);
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Fact]
    public async Task MissingAllowanceFailsRequiredHandlingInsteadOfSilentlySkippingIt()
    {
        string connection = await SetupAsync(openAllowance: false);
        await using var provider = Provider(connection);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(First, 1), Token)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        Assert.Equal(0, Events(scope).PendingCount);
        await transaction.RollbackAsync(CancellationToken.None);
        await using var read = DomainEventComposition.CreateScope(provider);
        Assert.False(
            (
                await read
                    .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                    .ReadAsync(First, Token)
            )!.IsSubmitted
        );
    }

    private static ServiceProvider Provider(string connection, params IInterceptor[] interceptors)
    {
        var services = DomainEventComposition.CreateServices(connection);
        if (interceptors.Length > 0)
            services.AddDbContext<SalesDbContext>(options => options.AddInterceptors(interceptors));

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private async Task<string> SetupAsync(
        decimal firstAmount = 40,
        decimal? secondAmount = null,
        bool openAllowance = true,
        decimal ceiling = 100
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await DomainEventComposition.SetupAsync(provider, Token);
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        if (openAllowance)
            await scope
                .ServiceProvider.GetRequiredService<ICustomerOrderAllowances>()
                .OpenAsync(SalesDemoSeed.AlphaCustomerId, "EUR", ceiling, Token);

        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).CreateAsync(DomainEventJourney.Request(First, firstAmount), Token)
        );
        if (secondAmount is not null)
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await Commands(scope)
                    .CreateAsync(DomainEventJourney.Request(Second, secondAmount.Value), Token)
            );

        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return connection;
    }

    private static async Task SubmitAndCommitAsync(ServiceProvider provider, Guid id)
    {
        await using var scope = DomainEventComposition.CreateScope(provider);
        var database = Db(scope);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(scope).SubmitAsync(new(id, 1), Token)
        );
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static async Task AssertStateAsync(
        ServiceProvider provider,
        Guid id,
        bool submitted,
        decimal reserved,
        long version,
        long? orderVersion = null
    )
    {
        await using var read = DomainEventComposition.CreateScope(provider);
        var order = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadAsync(id, Token);
        Assert.NotNull(order);
        Assert.Equal(submitted, order.IsSubmitted);
        Assert.Equal(orderVersion ?? (submitted ? 2 : 1), order.Version);
        var allowance = await read
            .ServiceProvider.GetRequiredService<ICustomerOrderAllowances>()
            .ReadAsync(SalesDemoSeed.AlphaCustomerId, "EUR", Token);
        Assert.NotNull(allowance);
        Assert.Equal(reserved, allowance.Reserved);
        Assert.Equal(version, allowance.Version);
    }

    private static Task SaveAsync(SalesDbContext database, string kind)
    {
        if (kind == "sync")
            database.SaveChanges();
        else if (kind == "sync-false")
            database.SaveChanges(false);
        else if (kind == "async")
            return database.SaveChangesAsync(Token);
        else
            return database.SaveChangesAsync(false, Token);

        return Task.CompletedTask;
    }
}
