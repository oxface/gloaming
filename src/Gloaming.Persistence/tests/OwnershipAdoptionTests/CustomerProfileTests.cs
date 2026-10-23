using Gloaming.Samples.Wholesale.PersistenceDemo.Sales;
using Gloaming.Tenancy;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.PersistenceDemo.Tests;

public sealed class CustomerProfileTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private const string Alpha = "wholesale-alpha";
    private const string Beta = "wholesale-beta";
    private static readonly Guid CustomerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AddressId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task ExplicitTransactionIsRequiredAndTheCallerChoosesWhenChangesCommit()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(scope);
            CustomerProfileChange change = Change(1, "Updated name", "Updated street");
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CustomerProfileChanges.ApplyAsync(sales, change, Token)
            );
            Assert.Equal(
                "Begin a caller-owned Sales transaction before applying a profile change.",
                failure.Message
            );

            await using var transaction = await sales.Database.BeginTransactionAsync(Token);
            Assert.Equal(2, await CustomerProfileChanges.ApplyAsync(sales, change, Token));
            Assert.Equal(("Alpha original", 1L, "Alpha street"), await ReadAsync(provider, Alpha));
            await transaction.CommitAsync(Token);
        }
        Assert.Equal(("Updated name", 2L, "Updated street"), await ReadAsync(provider, Alpha));
        Assert.Equal(("Beta original", 1L, "Beta street"), await ReadAsync(provider, Beta));
    }

    [Fact]
    public async Task AStaleRequestCannotOverwriteTheCommittedProfileEvenWhenTheServerReloadsIt()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        long firstObserved = (await ReadAsync(provider, Alpha)).Version;
        long secondObserved = (await ReadAsync(provider, Alpha)).Version;
        Assert.Equal(1, firstObserved);
        Assert.Equal(firstObserved, secondObserved);
        await using (AsyncServiceScope first = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(first);
            await using var transaction = await sales.Database.BeginTransactionAsync(Token);
            await CustomerProfileChanges.ApplyAsync(
                sales,
                Change(firstObserved, "Winner", "Winner street"),
                Token
            );
            await transaction.CommitAsync(Token);
        }
        await using (AsyncServiceScope second = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(second);
            await using var transaction = await sales.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                CustomerProfileChanges.ApplyAsync(
                    sales,
                    Change(secondObserved, "Loser", "Loser street"),
                    Token
                )
            );
            await transaction.RollbackAsync(CancellationToken.None);
        }
        Assert.Equal(("Winner", 2L, "Winner street"), await ReadAsync(provider, Alpha));
    }

    [Fact]
    public async Task ASecondSaveFaultRollsBackTheFirstSqlWriteAndAFreshOperationCanSucceed()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope failed = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(failed);
            await using var transaction = await sales.Database.BeginTransactionAsync(Token);
            // Fault setup violates the actual address constraint after the customer save.
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                CustomerProfileChanges.ApplyAsync(sales, Change(1, "Staged name", null!), Token)
            );
            var databaseFailure = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.NotNullViolation, databaseFailure.SqlState);
            Assert.Equal(nameof(CustomerAddressReference.AddressLine), databaseFailure.ColumnName);
            var staged = await sales
                .Customers.AsNoTracking()
                .Select(row => new { row.DisplayName, row.Version })
                .SingleAsync(Token);
            Assert.Equal("Staged name", staged.DisplayName);
            Assert.Equal(2, staged.Version);
            await transaction.RollbackAsync(CancellationToken.None);
        }
        Assert.Equal(("Alpha original", 1L, "Alpha street"), await ReadAsync(provider, Alpha));
        await using (AsyncServiceScope recovered = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(recovered);
            await using var transaction = await sales.Database.BeginTransactionAsync(Token);
            await CustomerProfileChanges.ApplyAsync(
                sales,
                Change(1, "Recovered", "Recovered street"),
                Token
            );
            await transaction.CommitAsync(Token);
        }
        Assert.Equal(("Recovered", 2L, "Recovered street"), await ReadAsync(provider, Alpha));
    }

    [Fact]
    public async Task CancellationAfterTheFirstSaveStillAllowsExplicitRollback()
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var cancellation = new CancelAfterSave(request);
        await using ServiceProvider provider = await SeededProviderAsync(cancellation);
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(scope);
            await using var transaction = await sales.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CustomerProfileChanges.ApplyAsync(
                    sales,
                    Change(1, "Canceled name", "Canceled street"),
                    request.Token
                )
            );
            Assert.Equal(1, cancellation.SavedOperationCount);
            var staged = await sales
                .Customers.AsNoTracking()
                .Select(row => new { row.DisplayName, row.Version })
                .SingleAsync(Token);
            Assert.Equal("Canceled name", staged.DisplayName);
            Assert.Equal(2, staged.Version);
            await transaction.RollbackAsync(CancellationToken.None);
        }
        Assert.Equal(("Alpha original", 1L, "Alpha street"), await ReadAsync(provider, Alpha));
    }

    [Fact]
    public async Task ExistingCustomerAndAddressGainAnInitialVersionBeforeTheNewOperationRuns()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using ServiceProvider provider = Provider(connection);
        await using (AsyncServiceScope setup = provider.CreateAsyncScope())
        {
            SalesDbContext sales = Sales(setup);
            await sales.GetService<IMigrator>().MigrateAsync("CustomerAddresses", Token);
            // Retained E2.3 column shapes have no Version; this is historical fixture setup.
            await sales.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sales.customer_reference ("Id", "OrganizationKey", "Code", "DisplayName")
                VALUES ({CustomerId}, {Alpha}, {"BUYER"}, {"Alpha original"})
                """,
                Token
            );
            await sales.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO sales.customer_address_reference ("Id", "OrganizationKey", "CustomerId", "AddressLine")
                VALUES ({AddressId}, {Alpha}, {CustomerId}, {"Alpha street"})
                """,
                Token
            );
            await sales.Database.MigrateAsync(Token);
            Assert.Equal(
                sales.GetService<IMigrationsAssembly>().Migrations.Keys,
                await sales.Database.GetAppliedMigrationsAsync(Token)
            );
        }
        Assert.Equal(("Alpha original", 1L, "Alpha street"), await ReadAsync(provider, Alpha));
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(scope);
            await using var transaction = await sales.Database.BeginTransactionAsync(Token);
            await CustomerProfileChanges.ApplyAsync(
                sales,
                Change(1, "Migrated", "Migrated street"),
                Token
            );
            await transaction.CommitAsync(Token);
        }
        Assert.Equal(("Migrated", 2L, "Migrated street"), await ReadAsync(provider, Alpha));
    }

    private async Task<ServiceProvider> SeededProviderAsync(IInterceptor? interceptor = null)
    {
        ServiceProvider provider = Provider(await postgres.CreateDatabaseAsync(Token), interceptor);
        try
        {
            await using (AsyncServiceScope setup = provider.CreateAsyncScope())
                await Sales(setup).Database.MigrateAsync(Token);
            foreach (string organization in new[] { Alpha, Beta })
            {
                Guid customerId = organization == Alpha ? CustomerId : Guid.NewGuid();
                Guid addressId = organization == Alpha ? AddressId : Guid.NewGuid();
                await using AsyncServiceScope scope = Scope(provider, organization);
                SalesDbContext sales = Sales(scope);
                sales.Customers.Add(
                    new CustomerReference
                    {
                        Id = customerId,
                        OrganizationKey = organization,
                        Code = "BUYER",
                        DisplayName = organization == Alpha ? "Alpha original" : "Beta original",
                        Version = 1,
                    }
                );
                await sales.SaveChangesAsync(Token);
                sales.CustomerAddresses.Add(
                    new CustomerAddressReference
                    {
                        Id = addressId,
                        OrganizationKey = organization,
                        CustomerId = customerId,
                        AddressLine = organization == Alpha ? "Alpha street" : "Beta street",
                    }
                );
                await sales.SaveChangesAsync(Token);
            }
            return provider;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private static ServiceProvider Provider(string connection, IInterceptor? interceptor = null)
    {
        ServiceCollection services = DemoComposition.CreateServices(connection);
        if (interceptor is not null)
            services.AddDbContext<SalesDbContext>(options => options.AddInterceptors(interceptor));
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private static AsyncServiceScope Scope(ServiceProvider provider, string organization)
    {
        AsyncServiceScope scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        return scope;
    }

    private static SalesDbContext Sales(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<SalesDbContext>();

    private static async Task<(string Name, long Version, string Address)> ReadAsync(
        ServiceProvider provider,
        string organization
    )
    {
        await using AsyncServiceScope scope = Scope(provider, organization);
        SalesDbContext sales = Sales(scope);
        CustomerReference customer = await sales.Customers.SingleAsync(Token);
        CustomerAddressReference address = await sales.CustomerAddresses.SingleAsync(Token);
        return (customer.DisplayName, customer.Version, address.AddressLine);
    }

    private static CustomerProfileChange Change(long version, string name, string address) =>
        new(CustomerId, AddressId, version, name, address);

    private sealed class CancelAfterSave(CancellationTokenSource request) : SaveChangesInterceptor
    {
        public int SavedOperationCount { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (cancellationToken == request.Token)
            {
                SavedOperationCount++;
                request.Cancel();
            }
            return ValueTask.FromResult(result);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
