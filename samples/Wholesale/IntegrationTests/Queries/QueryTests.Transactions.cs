using System.Data;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Queries;

public sealed partial class QueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerSelectsWhetherCountAndPageShareASnapshot(bool repeatable)
    {
        string connection = await SeedAsync();
        await using var writer = Provider(connection);
        var probe = new QueryProbe(() => CreateAsync(writer, CqrsDemoData.Draft(Id(70), 50)));
        await using var reader = Provider(connection, probe);
        await using (var scope = CqrsComposition.CreateScope(reader))
        {
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                repeatable ? IsolationLevel.RepeatableRead : IsolationLevel.ReadCommitted,
                Token
            );
            var page = await scope
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .SearchAsync(Filter with { Limit = 100 }, Token);
            Assert.Equal(3, page.TotalCount);
            Assert.Equal(
                repeatable
                    ? new[] { Id(20), Id(30), Id(40) }
                    : new[] { Id(20), Id(30), Id(70), Id(40) },
                page.Items.Select(item => item.Id)
            );
            await transaction.CommitAsync(Token);
        }

        await using var fresh = CqrsComposition.CreateScope(reader);
        var current = await fresh
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .SearchAsync(Filter with { Limit = 100 }, Token);
        Assert.Equal(4, current.TotalCount);
        Assert.Equal(4, current.Items.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueriesObserveNativeCommitAndRollbackRatherThanTrackedProposals(bool commit)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var write = CqrsComposition.CreateScope(provider))
        {
            var database = write.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var commands = write.ServiceProvider.GetRequiredService<IDraftOrderCommands>();
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await commands.ChangeLinesAsync(
                    new(Id(20), 1, [new DraftOrderLineChange.Change(Id(1), 4, 10)]),
                    Token
                )
            );
            var local = await write
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .SearchAsync(Filter with { Limit = 100 }, Token);
            Assert.Equal(40, local.Items.Single(row => row.Id == Id(20)).Total);
            await database.SaveChangesAsync(Token);
            await using (var observer = CqrsComposition.CreateScope(provider))
            {
                var outside = await observer
                    .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                    .SearchAsync(Filter with { Limit = 100 }, Token);
                Assert.Equal(40, outside.Items.Single(row => row.Id == Id(20)).Total);
            }

            if (commit)
                await transaction.CommitAsync(Token);
            else
                await transaction.RollbackAsync(Token);
        }

        await using var read = CqrsComposition.CreateScope(provider);
        var result = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .SearchAsync(Filter with { Limit = 100 }, Token);
        var order = result.Items.Single(row => row.Id == Id(20));
        Assert.Equal(commit ? 60 : 40, order.Total);
        Assert.Equal(commit ? 2 : 1, order.Version);
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task RejectedAndStaleCommandsDoNotChangeTheReadView()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var rejected = CqrsComposition.CreateScope(provider))
        {
            var database = rejected.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var commands = rejected.ServiceProvider.GetRequiredService<IDraftOrderCommands>();
            Assert.Equal(
                new DraftOrderChangeResult.LimitExceeded(100, 110),
                await commands.ChangeLinesAsync(
                    new(Id(20), 1, [new DraftOrderLineChange.Change(Id(1), 9, 10)]),
                    Token
                )
            );
            Assert.IsType<DraftOrderChangeResult.Conflict>(
                await commands.ChangeLinesAsync(
                    new(Id(20), 2, [new DraftOrderLineChange.Change(Id(1), 3, 10)]),
                    Token
                )
            );
            Assert.False(database.ChangeTracker.HasChanges());
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using var read = CqrsComposition.CreateScope(provider);
        var row = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadSummaryAsync(Id(20), Token);
        Assert.NotNull(row);
        Assert.Equal(1, row.Version);
        Assert.Equal(40, row.Total);
        Assert.Equal(2, row.LineCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tenantless")]
    public async Task SearchRequiresAnExplicitSelectedTenant(string establishment)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var scope = provider.CreateAsyncScope();
        if (establishment == "tenantless")
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());

        var queries = scope.ServiceProvider.GetRequiredService<IDraftOrderQueries>();
        if (establishment == "tenantless")
            await Assert.ThrowsAsync<TenantRequiredException>(() =>
                queries.SearchAsync(new(), Token)
            );
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queries.SearchAsync(new(), Token)
            );
    }

    [Theory]
    [InlineData("null")]
    [InlineData("name")]
    [InlineData("currency")]
    [InlineData("minimum")]
    [InlineData("offset")]
    [InlineData("limit-zero")]
    [InlineData("limit-large")]
    public async Task InvalidSearchInputFailsBeforeDatabaseExecution(string invalid)
    {
        var probe = new QueryProbe();
        // No live database is necessary: guards must reject input before executing SQL.
        await using var provider = Provider("Host=localhost;Database=unused", probe);
        await using var scope = CqrsComposition.CreateScope(provider);
        SearchDraftOrders request = invalid switch
        {
            "null" => null!,
            "name" => new(CustomerNameContains: " "),
            "currency" => new(Currency: " "),
            "minimum" => new(MinimumTotal: -1),
            "offset" => new(Offset: -1),
            "limit-zero" => new(Limit: 0),
            "limit-large" => new(Limit: 101),
            _ => throw new InvalidOperationException(),
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            scope
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .SearchAsync(request, Token)
        );
        Assert.Empty(probe.Commands);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutTrackingOrWrites()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var scope = CqrsComposition.CreateScope(provider);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .SearchAsync(new(), cancelled.Token)
        );
        Assert.Empty(
            scope.ServiceProvider.GetRequiredService<SalesDbContext>().ChangeTracker.Entries()
        );
    }
}
