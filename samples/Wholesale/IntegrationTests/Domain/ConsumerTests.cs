using Gloaming.Auditing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.Ddd;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Domain;

public sealed class ConsumerTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExecutableJourneyCommitsBothModelsAndRejectsPartialCandidates()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        await DddJourney.RunAsync(connection, output, Token);
        Assert.Contains("invalid address rejected", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("overflowing total rejected", output.ToString(), StringComparison.Ordinal);

        await using var provider = Provider(connection);
        await using var read = DddComposition.CreateScope(provider);
        Assert.Equal(
            new CustomerProfile(
                SalesDemoSeed.AlphaCustomerId,
                SalesDemoSeed.AlphaAddressId,
                "DEMO-CUSTOMER",
                "D1 Customer",
                "D1 Street",
                2
            ),
            await read
                .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                .ReadAsync(SalesDemoSeed.AlphaCustomerId, Token)
        );
        var current = await read
            .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
            .ReadCurrentAsync(DddJourney.OrderId, Token);
        Assert.NotNull(current);
        Assert.Equal(2, current.Version);
        Assert.Equal("D1-ORDER", current.Code);
        Assert.Equal("EUR", current.Currency);
        Assert.Equal(37.5m, current.Total);
        Assert.Equal(new PurchaseOrderLine("ITEM-1", 3, 12.5m), Assert.Single(current.Lines));
        var history = await read
            .ServiceProvider.GetRequiredService<IPurchaseOrderHistory>()
            .ReadCurrentAsync(DddJourney.OrderId, Token);
        Assert.NotNull(history);
        Assert.Equal(2, history.Version);
        Assert.Equal(37.5m, history.Total);
        Assert.Equal(new PurchaseOrderLine("ITEM-1", 3, 12.5m), Assert.Single(history.Lines));
        Assert.Single(
            await read
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Set<AuditRecord>()
                .ToArrayAsync(Token)
        );
        Assert.False(
            read.ServiceProvider.GetRequiredService<SalesDbContext>()
                .Database.HasPendingModelChanges()
        );
        Assert.False(
            read.ServiceProvider.GetRequiredService<PurchasingDbContext>()
                .Database.HasPendingModelChanges()
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidSalesDetailsRejectBeforeLoadingOrTrackingEitherRow(bool invalidName)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var write = DddComposition.CreateScope(provider);
        var profiles = write.ServiceProvider.GetRequiredService<ICustomerProfiles>();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            profiles.ChangeAsync(
                Edit(1, invalidName ? " " : "Replacement", invalidName ? "Replacement" : " "),
                Token
            )
        );
        Assert.Empty(
            write.ServiceProvider.GetRequiredService<SalesDbContext>().ChangeTracker.Entries()
        );
        await AssertOriginalAsync(provider);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task MismatchedExpectedVersionCannotReplaceACommittedWinner(long expectedVersion)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        CustomerProfile winner;
        await using (var first = DddComposition.CreateScope(provider))
        {
            var result = await first
                .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                .ChangeAsync(Edit(1, "Winner", "Winner street"), Token);
            winner = Assert.IsType<ProfileChangeResult.Updated>(result).Profile;
        }

        await using (var stale = DddComposition.CreateScope(provider))
        {
            var result = await stale
                .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                .ChangeAsync(Edit(expectedVersion, "Loser", "Loser street"), Token);
            Assert.IsType<ProfileChangeResult.Conflict>(result);
            Assert.False(
                stale
                    .ServiceProvider.GetRequiredService<SalesDbContext>()
                    .ChangeTracker.HasChanges()
            );
        }

        await using var read = DddComposition.CreateScope(provider);
        Assert.Equal(
            winner,
            await read
                .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                .ReadAsync(SalesDemoSeed.AlphaCustomerId, Token)
        );
        var audit = Assert.Single(
            await read
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Set<AuditRecord>()
                .ToArrayAsync(Token)
        );
        Assert.Equal(1, audit.Details.GetProperty("PreviousVersion").GetInt64());
        Assert.Equal(2, audit.Details.GetProperty("Version").GetInt64());
    }

    [Fact]
    public async Task FailedPersistenceRollsBackTheAcceptedSalesCandidateAndFreshEditRecovers()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var fault = DddComposition.CreateScope(provider))
        {
            await fault
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Database.ExecuteSqlRawAsync(
                    "ALTER TABLE sales.customer_addresses ADD CONSTRAINT d1_fault CHECK (address_line <> 'Blocked')",
                    Token
                );
        }

        await using (var write = DddComposition.CreateScope(provider))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                write
                    .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                    .ChangeAsync(Edit(1, "Must roll back", "Blocked"), Token)
            );
        }

        await AssertOriginalAsync(provider);
        await using var retry = DddComposition.CreateScope(provider);
        var updated = Assert.IsType<ProfileChangeResult.Updated>(
            await retry
                .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                .ChangeAsync(Edit(1, "Recovered", "Recovered street"), Token)
        );
        Assert.Equal(2, updated.Profile.Version);
        Assert.Equal("Recovered", updated.Profile.DisplayName);
    }

    private async Task<string> SeedAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using var scope = DddComposition.CreateScope(provider);
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await database.Database.MigrateAsync(Token);
        SalesDemoSeed.Stage(database);
        await database.SaveChangesAsync(Token);
        return connection;
    }

    private static ServiceProvider Provider(string connection) =>
        DddComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

    private static CustomerProfileChange Edit(long version, string name, string address) =>
        new(SalesDemoSeed.AlphaCustomerId, SalesDemoSeed.AlphaAddressId, version, name, address);

    private static async Task AssertOriginalAsync(ServiceProvider provider)
    {
        await using var read = DddComposition.CreateScope(provider);
        Assert.Equal(
            new CustomerProfile(
                SalesDemoSeed.AlphaCustomerId,
                SalesDemoSeed.AlphaAddressId,
                "DEMO-CUSTOMER",
                "Alpha Customer",
                "Alpha Street",
                1
            ),
            await read
                .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                .ReadAsync(SalesDemoSeed.AlphaCustomerId, Token)
        );
        Assert.Empty(
            await read
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Set<AuditRecord>()
                .ToArrayAsync(Token)
        );
    }
}
