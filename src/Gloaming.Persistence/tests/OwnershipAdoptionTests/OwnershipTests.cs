using System.Diagnostics;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.PersistenceDemo.Inventory;
using Gloaming.Tenancy;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.PersistenceDemo.Tests;

public sealed class OwnershipTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string Alpha = "wholesale-alpha";
    private const string Beta = "wholesale-beta";
    private static readonly Guid AlphaId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BetaId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task ActualConsumerSharesACachedModelButReadsOnlyItsCurrentOrganization()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using AsyncServiceScope alphaScope = Scope(provider, Alpha);
        await using AsyncServiceScope betaScope = Scope(provider, Beta);
        InventoryDbContext alpha = Inventory(alphaScope);
        InventoryDbContext beta = Inventory(betaScope);
        Assert.Same(alpha.Model, beta.Model);
        Task<int> alphaRead = alpha.Stock.Select(row => row.Quantity).SingleAsync(Token);
        Task<int> betaRead = beta.Stock.Select(row => row.Quantity).SingleAsync(Token);
        await Task.WhenAll(alphaRead, betaRead);
        Assert.Equal(42, await alphaRead);
        Assert.Equal(7, await betaRead);
        Assert.Null(await alpha.Stock.SingleOrDefaultAsync(row => row.Id == BetaId, Token));
        int[] includingDeleted = await alpha
            .Stock.IgnoreQueryFilters(["SoftDeletion"])
            .OrderBy(row => row.Quantity)
            .Select(row => row.Quantity)
            .ToArrayAsync(Token);
        Assert.Equal([42, 99], includingDeleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnestablishedOrDeliberatelyTenantlessOwnedReadsAndWritesFail(bool tenantless)
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        if (tenantless)
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.Tenantless());
        InventoryDbContext context = Inventory(scope);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            context.Stock.ToArrayAsync(Token)
        );
        context.Stock.Add(Row(Guid.NewGuid(), Alpha, "NEW", 1));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            context.SaveChangesAsync(Token)
        );
        Assert.Equal(42, (await ReadAsync(provider, Alpha, AlphaId))!.Quantity);
    }

    [Fact]
    public async Task GlobalDataCanBeSavedWithoutInitializingTenancy()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            InventoryDbContext context = Inventory(scope);
            context.Categories.Add(
                new ReferenceCategory { Id = Guid.NewGuid(), Name = "global-category" }
            );
            await context.SaveChangesAsync(Token);
        }
        await using AsyncServiceScope fresh = provider.CreateAsyncScope();
        Assert.Equal(
            "global-category",
            (await Inventory(fresh).Categories.SingleAsync(Token)).Name
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task EveryNativeSaveOverloadRejectsForeignInsertsWithoutStamping(int overload)
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using AsyncServiceScope scope = Scope(provider, Alpha);
        InventoryDbContext context = Inventory(scope);
        StockReference foreign = Row(Guid.NewGuid(), Beta, "NEW", 1);
        context.Stock.Add(foreign);
        var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
            SaveAsync(context, overload)
        );
        Assert.Equal(TenantOwnershipFailure.ForeignOwner, failure.Reason);
        Assert.Equal(Beta, foreign.OrganizationKey);
        Assert.Null(await ReadAsync(provider, Beta, foreign.Id));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task BothConsumerSaveOverridesPersistCorrectlyOwnedInserts(int overload)
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        Guid id = Guid.NewGuid();
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            InventoryDbContext context = Inventory(scope);
            context.Stock.Add(Row(id, Alpha, "NEW", 17));
            await SaveAsync(context, overload);
        }
        Assert.Equal(17, (await ReadAsync(provider, Alpha, id))!.Quantity);
        Assert.Null(await ReadAsync(provider, Beta, id));
    }

    [Fact]
    public async Task MissingOwnerFailsWithoutAutomaticPopulation()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using AsyncServiceScope scope = Scope(provider, Alpha);
        InventoryDbContext context = Inventory(scope);
        StockReference row = Row(Guid.NewGuid(), null!, "NEW", 1);
        context.Stock.Add(row);
        var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
            context.SaveChangesAsync(Token)
        );
        Assert.Equal(TenantOwnershipFailure.MissingOwner, failure.Reason);
        Assert.Null(row.OrganizationKey);
    }

    [Fact]
    public async Task OwnershipChangeIsDetectedWithAutomaticChangeDetectionDisabled()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            InventoryDbContext context = Inventory(scope);
            StockReference row = await context.Stock.SingleAsync(Token);
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            row.OrganizationKey = Beta;
            var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                context.SaveChangesAsync(Token)
            );
            Assert.Equal(TenantOwnershipFailure.OwnershipChanged, failure.Reason);
        }
        Assert.Equal(Alpha, (await ReadAsync(provider, Alpha, AlphaId))!.OrganizationKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignStoredOwnerOnATrackedEntryIsRejected(bool delete)
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            InventoryDbContext context = Inventory(scope);
            StockReference foreign = await context
                .Stock.IgnoreQueryFilters()
                .SingleAsync(row => row.Id == BetaId, Token);
            if (delete)
                context.Remove(foreign);
            else
                foreign.Quantity = 123;
            var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                context.SaveChangesAsync(Token)
            );
            Assert.Equal(TenantOwnershipFailure.ForeignOwner, failure.Reason);
        }
        Assert.Equal(7, (await ReadAsync(provider, Beta, BetaId))!.Quantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentTenantPlusForeignDetachedIdCannotUpdateOrDeleteStoredData(bool delete)
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            InventoryDbContext context = Inventory(scope);
            StockReference forged = Row(BetaId, Alpha, "WIDGET", 123);
            if (delete)
                context.Remove(forged);
            else
                context.Update(forged);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                context.SaveChangesAsync(Token)
            );
        }
        StockReference actual = (await ReadAsync(provider, Beta, BetaId))!;
        Assert.Equal(Beta, actual.OrganizationKey);
        Assert.Equal(7, actual.Quantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameTenantDetachedUpdatesAndDeletesWork(bool delete)
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            InventoryDbContext context = Inventory(scope);
            StockReference detached = Row(AlphaId, Alpha, "WIDGET", 18);
            if (delete)
                context.Remove(detached);
            else
                context.Update(detached);
            await context.SaveChangesAsync(Token);
        }
        StockReference? actual = await ReadAsync(provider, Alpha, AlphaId);
        if (delete)
            Assert.Null(actual);
        else
            Assert.Equal(18, actual!.Quantity);
        Assert.Equal(7, (await ReadAsync(provider, Beta, BetaId))!.Quantity);
    }

    [Fact]
    public async Task ForgedForeignOriginalOwnerIsRejectedBeforeTheNativeSave()
    {
        await using ServiceProvider provider = await SeededProviderAsync();
        await using (AsyncServiceScope scope = Scope(provider, Alpha))
        {
            InventoryDbContext context = Inventory(scope);
            StockReference forged = Row(BetaId, Alpha, "WIDGET", 123);
            context.Update(forged);
            context.Entry(forged).Property(row => row.OrganizationKey).OriginalValue = Beta;
            var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                context.SaveChangesAsync(Token)
            );
            Assert.Equal(TenantOwnershipFailure.OwnershipChanged, failure.Reason);
        }
        Assert.Equal(7, (await ReadAsync(provider, Beta, BetaId))!.Quantity);
    }

    [Fact]
    public async Task FiniteConsoleRunsAgainstAFreshDatabaseAndCanBeRepeated()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        string[] expected =
        [
            "wholesale-alpha: WIDGET availability=42",
            "wholesale-alpha: BUYER customer=Alpha Retail",
            "wholesale-beta: WIDGET availability=7",
            "wholesale-beta: BUYER customer=Beta Retail",
        ];
        Assert.Equal(expected, await RunAsync());
        Assert.Equal(expected, await RunAsync());

        async Task<string[]> RunAsync()
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(typeof(DemoComposition).Assembly.Location);
            start.Environment["WHOLESALE_DEMO_CONNECTION_STRING"] = connection;
            using var process =
                Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the demo.");
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(Token);
                Task<string> errors = process.StandardError.ReadToEndAsync(Token);
                await process.WaitForExitAsync(Token).WaitAsync(TimeSpan.FromSeconds(30), Token);
                Assert.True(process.ExitCode == 0, await errors);
                return (await output).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
    }

    private async Task<ServiceProvider> SeededProviderAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        ServiceProvider provider = DemoComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        try
        {
            await using (AsyncServiceScope setup = provider.CreateAsyncScope())
                await Inventory(setup).Database.MigrateAsync(Token);
            await using (AsyncServiceScope alpha = Scope(provider, Alpha))
            {
                Inventory(alpha)
                    .Stock.AddRange(
                        Row(AlphaId, Alpha, "WIDGET", 42),
                        new StockReference
                        {
                            Id = Guid.NewGuid(),
                            OrganizationKey = Alpha,
                            Sku = "DISABLED",
                            Quantity = 99,
                            IsDeleted = true,
                        }
                    );
                await Inventory(alpha).SaveChangesAsync(Token);
            }
            await using (AsyncServiceScope beta = Scope(provider, Beta))
            {
                Inventory(beta).Stock.Add(Row(BetaId, Beta, "WIDGET", 7));
                await Inventory(beta).SaveChangesAsync(Token);
            }
            return provider;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private static AsyncServiceScope Scope(ServiceProvider provider, string organization)
    {
        AsyncServiceScope scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        return scope;
    }

    private static InventoryDbContext Inventory(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

    private static async Task<StockReference?> ReadAsync(
        ServiceProvider provider,
        string organization,
        Guid id
    )
    {
        await using AsyncServiceScope scope = Scope(provider, organization);
        return await Inventory(scope).Stock.SingleOrDefaultAsync(row => row.Id == id, Token);
    }

    private static StockReference Row(Guid id, string organization, string sku, int quantity) =>
        new()
        {
            Id = id,
            OrganizationKey = organization,
            Sku = sku,
            Quantity = quantity,
        };

    private static Task<int> SaveAsync(InventoryDbContext context, int overload) =>
        overload switch
        {
            1 => Task.FromResult(context.SaveChanges()),
            2 => Task.FromResult(context.SaveChanges(false)),
            3 => context.SaveChangesAsync(Token),
            4 => context.SaveChangesAsync(false, Token),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
