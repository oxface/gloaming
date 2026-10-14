using Gloaming.EntityFrameworkCoreTests;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Tests.Infrastructure;

namespace Gloaming.PersistenceTests;

public sealed class GuidOwnershipTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid Alpha = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Beta = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AlphaRow = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BetaRow = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task GuidConsumerSharesAModelWithoutSharingTheWorkspaceAndComposesFilters()
    {
        DbContextOptions options = await SeedAsync();
        await using var alpha = new GuidConsumerContext(options, Alpha);
        await using var beta = new GuidConsumerContext(options, Beta);
        Assert.Same(alpha.Model, beta.Model);
        Task<string[]> alphaRead = alpha.Accounts.Select(row => row.Label).ToArrayAsync(Token);
        Task<string[]> betaRead = beta.Accounts.Select(row => row.Label).ToArrayAsync(Token);
        await Task.WhenAll(alphaRead, betaRead);
        Assert.Equal(["alpha"], await alphaRead);
        Assert.Equal(["beta"], await betaRead);
        Assert.Equal(
            ["alpha", "archived-alpha"],
            await alpha
                .Accounts.IgnoreQueryFilters(["Archived"])
                .OrderBy(row => row.Label)
                .Select(row => row.Label)
                .ToArrayAsync(Token)
        );
        Assert.Null(await alpha.Accounts.SingleOrDefaultAsync(row => row.Id == BetaRow, Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentWorkspaceOnADetachedForeignIdCannotUpdateOrDeleteIt(bool delete)
    {
        DbContextOptions options = await SeedAsync();
        await using (var alpha = new GuidConsumerContext(options, Alpha))
        {
            var forged = new AccountReference
            {
                Id = BetaRow,
                WorkspaceKey = Alpha,
                Label = "forged",
            };
            if (delete)
                alpha.Remove(forged);
            else
                alpha.Update(forged);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                alpha.SaveChangesAsync(Token)
            );
        }
        await using var beta = new GuidConsumerContext(options, Beta);
        Assert.Equal("beta", (await beta.Accounts.SingleAsync(Token)).Label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameWorkspaceDetachedUpdatesAndDeletesRemainNativeOperations(bool delete)
    {
        DbContextOptions options = await SeedAsync();
        await using (var alpha = new GuidConsumerContext(options, Alpha))
        {
            var detached = new AccountReference
            {
                Id = AlphaRow,
                WorkspaceKey = Alpha,
                Label = "changed",
            };
            if (delete)
                alpha.Remove(detached);
            else
                alpha.Update(detached);
            await alpha.SaveChangesAsync(Token);
        }
        await using var fresh = new GuidConsumerContext(options, Alpha);
        AccountReference? result = await fresh.Accounts.SingleOrDefaultAsync(
            row => row.Id == AlphaRow,
            Token
        );
        if (delete)
            Assert.Null(result);
        else
            Assert.Equal("changed", result!.Label);
    }

    [Fact]
    public async Task GlobalOnlySaveDoesNotRequireAWorkspaceOrAnyContextLibrary()
    {
        DbContextOptions options = await SeedAsync();
        await using (var global = new GuidConsumerContext(options, null))
        {
            global.Lookups.Add(new GlobalLookup { Id = Guid.NewGuid(), Label = "global" });
            await global.SaveChangesAsync(Token);
            Assert.Equal(0, global.KeyReads);
        }
        await using var fresh = new GuidConsumerContext(options, null);
        Assert.Equal("global", (await fresh.Lookups.SingleAsync(Token)).Label);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            fresh.Accounts.ToArrayAsync(Token)
        );
    }

    private async Task<DbContextOptions> SeedAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        DbContextOptions options = new DbContextOptionsBuilder().UseNpgsql(connection).Options;
        await using (var alpha = new GuidConsumerContext(options, Alpha))
        {
            await alpha.Database.EnsureCreatedAsync(Token);
            alpha.Accounts.AddRange(
                new AccountReference
                {
                    Id = AlphaRow,
                    WorkspaceKey = Alpha,
                    Label = "alpha",
                },
                new AccountReference
                {
                    Id = Guid.NewGuid(),
                    WorkspaceKey = Alpha,
                    Label = "archived-alpha",
                    Archived = true,
                }
            );
            await alpha.SaveChangesAsync(Token);
        }
        await using (var beta = new GuidConsumerContext(options, Beta))
        {
            beta.Accounts.Add(
                new AccountReference
                {
                    Id = BetaRow,
                    WorkspaceKey = Beta,
                    Label = "beta",
                }
            );
            await beta.SaveChangesAsync(Token);
        }
        return options;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
