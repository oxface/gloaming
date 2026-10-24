using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.PersistenceDemo.Sales;
using Gloaming.Tenancy;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.PersistenceDemo.Tests;

public sealed class CustomerRelationshipTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private const string Alpha = "wholesale-alpha";
    private const string Beta = "wholesale-beta";
    private static readonly Guid AlphaCustomer = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BetaCustomer = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherAlphaCustomer = Guid.Parse(
        "cccccccc-cccc-cccc-cccc-cccccccccccc"
    );
    private static readonly Guid AlphaAddress = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task DetachedParentIdsSupportTenantScopedChildrenAndSameTenantReparenting()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope beta = Scope(provider, Beta))
        {
            SalesDbContext sales = Sales(beta);
            Assert.Empty(await sales.CustomerAddresses.ToArrayAsync(Token));
            sales.CustomerAddresses.Add(Address(Guid.NewGuid(), Beta, BetaCustomer, "7 Dock Road"));
            await sales.SaveChangesAsync(Token);
            Assert.Equal(
                "7 Dock Road",
                (await sales.CustomerAddresses.SingleAsync(Token)).AddressLine
            );
        }
        await using (AsyncServiceScope alpha = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(alpha);
            CustomerAddressReference address = await sales.CustomerAddresses.SingleAsync(Token);
            Assert.Equal("42 Market Street", address.AddressLine);
            address.CustomerId = OtherAlphaCustomer;
            await sales.SaveChangesAsync(Token);
        }
        await using AsyncServiceScope fresh = Scope(provider, Alpha);
        Assert.Equal(
            OtherAlphaCustomer,
            (await Sales(fresh).CustomerAddresses.SingleAsync(Token)).CustomerId
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentTenantChildCannotReferenceAForeignParentByDetachedId(bool update)
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        Guid newId = Guid.NewGuid();
        await using (AsyncServiceScope alpha = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(alpha);
            CustomerAddressReference child = Address(
                update ? AlphaAddress : newId,
                Alpha,
                BetaCustomer,
                "Forged"
            );
            if (update)
                sales.CustomerAddresses.Update(child);
            else
                sales.CustomerAddresses.Add(child);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                sales.SaveChangesAsync(Token)
            );
            var databaseFailure = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, databaseFailure.SqlState);
            Assert.Equal(
                "FK_customer_address_reference_customer_tenant",
                databaseFailure.ConstraintName
            );
        }
        await using AsyncServiceScope fresh = Scope(provider, Alpha);
        CustomerAddressReference actual = await Sales(fresh).CustomerAddresses.SingleAsync(Token);
        Assert.Equal(AlphaAddress, actual.Id);
        Assert.Equal(AlphaCustomer, actual.CustomerId);
        Assert.Equal("42 Market Street", actual.AddressLine);
    }

    [Fact]
    public async Task ChildOwnershipIsValidatedBeforeForeignInsertsReachTheDatabase()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope alpha = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(alpha);
            sales.CustomerAddresses.Add(Address(Guid.NewGuid(), Beta, BetaCustomer, "Foreign"));
            var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                sales.SaveChangesAsync(Token)
            );
            Assert.Equal(TenantOwnershipFailure.ForeignOwner, failure.Reason);
        }
        await using AsyncServiceScope beta = Scope(provider, Beta);
        Assert.Empty(await Sales(beta).CustomerAddresses.ToArrayAsync(Token));
    }

    [Fact]
    public async Task CustomerDeletionRequiresExplicitChildRemoval()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope alpha = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(alpha);
            sales.Customers.Remove(Customer(AlphaCustomer, Alpha, "BUYER"));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                sales.SaveChangesAsync(Token)
            );
            var databaseFailure = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.RestrictViolation, databaseFailure.SqlState);
            Assert.Equal(
                "FK_customer_address_reference_customer_tenant",
                databaseFailure.ConstraintName
            );
        }
        await using (AsyncServiceScope fresh = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(fresh);
            Assert.True(await sales.Customers.AnyAsync(row => row.Id == AlphaCustomer, Token));
            sales.CustomerAddresses.Remove(await sales.CustomerAddresses.SingleAsync(Token));
            await sales.SaveChangesAsync(Token);
            sales.Customers.Remove(
                await sales.Customers.SingleAsync(row => row.Id == AlphaCustomer, Token)
            );
            await sales.SaveChangesAsync(Token);
        }
        await using AsyncServiceScope final = Scope(provider, Alpha);
        Assert.Empty(await Sales(final).CustomerAddresses.ToArrayAsync(Token));
        Assert.False(await Sales(final).Customers.AnyAsync(row => row.Id == AlphaCustomer, Token));
    }

    [Fact]
    public async Task DetachedCustomerEditsStillWorkWithOwnershipInAnAlternateKey()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope alpha = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(alpha);
            CustomerReference customer = Customer(AlphaCustomer, Alpha, "BUYER");
            customer.DisplayName = "Renamed";
            sales.Customers.Update(customer);
            await sales.SaveChangesAsync(Token);
        }
        await using AsyncServiceScope fresh = Scope(provider, Alpha);
        Assert.Equal(
            "Renamed",
            (
                await Sales(fresh).Customers.SingleAsync(row => row.Id == AlphaCustomer, Token)
            ).DisplayName
        );
    }

    [Fact]
    public async Task RelationshipMigrationPreservesAnExistingCustomerAndItsHistory()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using ServiceProvider provider = Provider(connection);
        await using (AsyncServiceScope setup = provider.CreateAsyncScope())
            await Sales(setup).GetService<IMigrator>().MigrateAsync("InitialSales", Token);
        await using (AsyncServiceScope alpha = Scope(provider, Alpha))
        {
            // The retained initial schema has no Version column; seed its original shape.
            await Sales(alpha)
                .Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO sales.customer_reference ("Id", "OrganizationKey", "Code", "DisplayName")
                    VALUES ({AlphaCustomer}, {Alpha}, {"BUYER"}, {"BUYER customer"})
                    """,
                    Token
                );
        }
        await using (AsyncServiceScope setup = provider.CreateAsyncScope())
            await Sales(setup).Database.MigrateAsync(Token);
        await using (AsyncServiceScope alpha = Scope(provider, Alpha))
        {
            SalesDbContext sales = Sales(alpha);
            CustomerReference existing = await sales.Customers.SingleAsync(Token);
            Assert.Equal(AlphaCustomer, existing.Id);
            Assert.Equal("BUYER", existing.Code);
            Assert.Equal("BUYER customer", existing.DisplayName);
            sales.CustomerAddresses.Add(
                Address(AlphaAddress, Alpha, existing.Id, "42 Market Street")
            );
            await sales.SaveChangesAsync(Token);
            Assert.Equal(
                sales.GetService<IMigrationsAssembly>().Migrations.Keys,
                await sales.Database.GetAppliedMigrationsAsync(Token)
            );
        }
        await using AsyncServiceScope fresh = Scope(provider, Alpha);
        Assert.Equal(
            AlphaCustomer,
            (await Sales(fresh).CustomerAddresses.SingleAsync(Token)).CustomerId
        );
    }

    private async Task<ServiceProvider> SeededProviderAsync()
    {
        ServiceProvider provider = Provider(await postgres.CreateDatabaseAsync(Token));
        try
        {
            await using (AsyncServiceScope setup = provider.CreateAsyncScope())
                await Sales(setup).Database.MigrateAsync(Token);
            await using (AsyncServiceScope alpha = Scope(provider, Alpha))
            {
                Sales(alpha)
                    .Customers.AddRange(
                        Customer(AlphaCustomer, Alpha, "BUYER"),
                        Customer(OtherAlphaCustomer, Alpha, "OTHER")
                    );
                await Sales(alpha).SaveChangesAsync(Token);
                Sales(alpha)
                    .CustomerAddresses.Add(
                        Address(AlphaAddress, Alpha, AlphaCustomer, "42 Market Street")
                    );
                await Sales(alpha).SaveChangesAsync(Token);
            }
            await using (AsyncServiceScope beta = Scope(provider, Beta))
            {
                Sales(beta).Customers.Add(Customer(BetaCustomer, Beta, "BUYER"));
                await Sales(beta).SaveChangesAsync(Token);
            }
            return provider;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private static ServiceProvider Provider(string connection) =>
        DemoComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

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

    private static CustomerReference Customer(Guid id, string organization, string code) =>
        new()
        {
            Id = id,
            OrganizationKey = organization,
            Code = code,
            DisplayName = code + " customer",
            Version = 1,
        };

    private static CustomerAddressReference Address(
        Guid id,
        string organization,
        Guid customerId,
        string line
    ) =>
        new()
        {
            Id = id,
            OrganizationKey = organization,
            CustomerId = customerId,
            AddressLine = line,
        };

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
