using System.Text.Json;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using JsonOwnershipConsumer;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gloaming.PersistenceTests;

public sealed class JsonOwnershipTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task IndependentConsumerJourney_RunsWithNativeContextAndNoTestSpecificMapping() =>
        await JsonOwnershipJourney.RunAsync(await postgres.CreateDatabaseAsync(Token), Token);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmutableJsonRecords_ReloadTrackAndSaveJsonOnlyChanges(bool asynchronous)
    {
        var (connection, id, _) = await SeedAsync();
        await using (var context = Open(connection, "alpha"))
        {
            var row = await context.Documents.SingleAsync(Token);
            Assert.Null(row.Draft.SelectedType);
            Assert.Equal(Priority.High, row.Draft.Requirements[0].Priority);
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            row.Content = row.Content with
            {
                Levels =
                [
                    row.Content.Levels[0] with
                    {
                        Description = "Edited level",
                    },
                    new("advanced", "Advanced level"),
                ],
            };
            row.Draft = row.Draft with
            {
                SelectedType = new("engineering", 3),
                References = [new("architecture", 2)],
                Requirements = [new("basic", Priority.Normal), new("advanced", null)],
            };
            row.Defaults =
            [
                row.Defaults[0] with
                {
                    Label = "Changed label",
                },
                new("advanced", "Advanced"),
            ];
            await SaveAsync(context, asynchronous);
            Assert.Equal(EntityState.Unchanged, context.Entry(row).State);
            Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        }

        await using (var context = Open(connection, "alpha"))
        {
            var row = await context.Documents.SingleAsync(Token);
            Assert.Equal(id, row.Id);
            Assert.Equal("unchanged scalar", row.Marker);
            Assert.Equal("Edited level", row.Content.Levels[0].Description);
            Assert.Equal(2, row.Content.Levels.Count);
            Assert.Equal(new SelectionValue("engineering", 3), row.Draft.SelectedType);
            Assert.Equal(
                new ReferenceValue("architecture", 2),
                Assert.Single(row.Draft.References)
            );
            Assert.Null(row.Draft.Requirements[1].Priority);
            Assert.Equal("Changed label", row.Defaults[0].Label);
            // Change exclusively inside the materialized list; no root property replacement.
            var levels = Assert.IsAssignableFrom<IList<LevelValue>>(row.Content.Levels);
            levels[0] = levels[0] with { Description = "Edited after reload" };
            row.Draft = row.Draft with { SelectedType = null };
            await SaveAsync(context, asynchronous);
        }

        await using var fresh = Open(connection, "alpha");
        var saved = await fresh.Documents.SingleAsync(Token);
        Assert.Null(saved.Draft.SelectedType);
        Assert.Equal("Edited after reload", saved.Content.Levels[0].Description);
        await using var native = new NpgsqlConnection(connection);
        await native.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT \"DraftJson\"::text FROM json_consumer.documents WHERE \"Id\" = @id",
            native
        );
        command.Parameters.AddWithValue("id", id);
        using var json = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(Token))!);
        Assert.Equal(
            "Normal",
            json.RootElement.GetProperty("Requirements")[0].GetProperty("Priority").GetString()
        );
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ForeignTrackedJsonOnlyChange_IsRejectedAfterReload(
        bool asynchronous,
        bool rootCollection
    )
    {
        var (connection, _, betaId) = await SeedAsync();
        await using var context = Open(connection, "alpha");
        var foreign = await context
            .Documents.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == betaId, Token);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        if (rootCollection)
            foreign.Defaults[0] = foreign.Defaults[0] with { Label = "Foreign edit" };
        else
            foreign.Content.Levels[0] = foreign.Content.Levels[0] with
            {
                Description = "Foreign edit",
            };

        var error = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
            SaveAsync(context, asynchronous)
        );
        Assert.Equal(TenantOwnershipFailure.ForeignOwner, error.Reason);
        await using var fresh = Open(connection, "beta");
        Assert.Equal(
            "Original level",
            (await fresh.Documents.SingleAsync(Token)).Content.Levels[0].Description
        );
        Assert.Equal("Basic", (await fresh.Documents.SingleAsync(Token)).Defaults[0].Label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TenantChangesAndForeignInsertRemainRejected(bool asynchronous)
    {
        var (connection, _, _) = await SeedAsync();
        await using (var context = Open(connection, "alpha"))
        {
            var row = await context.Documents.SingleAsync(Token);
            row.Content = row.Content with { Name = "Changed JSON" };
            row.Tenant = "beta";
            var error = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                SaveAsync(context, asynchronous)
            );
            Assert.Equal(TenantOwnershipFailure.OwnershipChanged, error.Reason);
        }

        await using (var context = Open(connection, "alpha"))
        {
            context.Add(new JsonDocumentRow { Id = Guid.NewGuid(), Tenant = "beta" });
            var error = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                SaveAsync(context, asynchronous)
            );
            Assert.Equal(TenantOwnershipFailure.ForeignOwner, error.Reason);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetachedForeignId_WithForgedCurrentTenant_FailsNativeConcurrency(
        bool asynchronous
    )
    {
        var (connection, _, betaId) = await SeedAsync();
        await using var context = Open(connection, "alpha");
        var forged = new JsonDocumentRow
        {
            Id = betaId,
            Tenant = "alpha",
            Content = new("Forged", [new("x", "Foreign edit")]),
        };
        context.Attach(forged);
        forged.Content = forged.Content with { Name = "Only JSON changed" };
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            SaveAsync(context, asynchronous)
        );
        await using var fresh = Open(connection, "beta");
        Assert.Equal("Original", (await fresh.Documents.SingleAsync(Token)).Content.Name);
    }

    [Fact]
    public async Task FiltersAndRollback_KeepOrdinaryOwnerSemantics()
    {
        var (connection, _, _) = await SeedAsync();
        await using (var context = Open(connection, "alpha"))
        {
            context.Add(
                new JsonDocumentRow
                {
                    Id = Guid.NewGuid(),
                    Tenant = "alpha",
                    Archived = true,
                }
            );
            await context.SaveChangesAsync(Token);
        }

        await using (var alpha = Open(connection, "alpha"))
        await using (var beta = Open(connection, "beta"))
        {
            Assert.Same(alpha.Model, beta.Model);
            Assert.Single(await alpha.Documents.ToArrayAsync(Token));
            Assert.Single(await beta.Documents.ToArrayAsync(Token));
            Assert.Equal(
                2,
                await alpha.Documents.IgnoreQueryFilters(["Archived"]).CountAsync(Token)
            );
            await using var transaction = await alpha.Database.BeginTransactionAsync(Token);
            var row = await alpha.Documents.SingleAsync(Token);
            row.Content = row.Content with { Name = "Rolled back" };
            await alpha.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }

        await using var fresh = Open(connection, "alpha");
        Assert.Equal("Original", (await fresh.Documents.SingleAsync(Token)).Content.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignDeleteIsRejected_AndSameTenantDeleteRemainsNative(bool asynchronous)
    {
        var (connection, _, betaId) = await SeedAsync();
        await using (var context = Open(connection, "alpha"))
        {
            context.Remove(
                await context.Documents.IgnoreQueryFilters().SingleAsync(x => x.Id == betaId, Token)
            );
            var error = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                SaveAsync(context, asynchronous)
            );
            Assert.Equal(TenantOwnershipFailure.ForeignOwner, error.Reason);
        }

        await using (var context = Open(connection, "alpha"))
        {
            context.Remove(await context.Documents.SingleAsync(Token));
            await SaveAsync(context, asynchronous);
        }

        await using var fresh = Open(connection, "alpha");
        Assert.Empty(await fresh.Documents.ToArrayAsync(Token));
        Assert.Equal(betaId, (await fresh.Documents.IgnoreQueryFilters().SingleAsync(Token)).Id);
    }

    private async Task<(string Connection, Guid AlphaId, Guid BetaId)> SeedAsync()
    {
        var connection = await postgres.CreateDatabaseAsync(Token);
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        await using (var alpha = Open(connection, "alpha"))
        {
            await alpha.Database.EnsureCreatedAsync(Token);
            alpha.Add(
                new JsonDocumentRow
                {
                    Id = alphaId,
                    Tenant = "alpha",
                    Defaults = [new("basic", "Basic")],
                }
            );
            await alpha.SaveChangesAsync(Token);
        }

        await using (var beta = Open(connection, "beta"))
        {
            beta.Add(
                new JsonDocumentRow
                {
                    Id = betaId,
                    Tenant = "beta",
                    Defaults = [new("basic", "Basic")],
                }
            );
            await beta.SaveChangesAsync(Token);
        }

        return (connection, alphaId, betaId);
    }

    private static JsonDocumentsContext Open(string connection, string tenant) =>
        new(new DbContextOptionsBuilder().UseNpgsql(connection).Options, tenant);

    private static Task<int> SaveAsync(JsonDocumentsContext context, bool asynchronous) =>
        asynchronous ? context.SaveChangesAsync(Token) : Task.FromResult(context.SaveChanges());

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
