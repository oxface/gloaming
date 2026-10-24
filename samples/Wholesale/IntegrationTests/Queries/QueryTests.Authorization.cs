using System.Data;
using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Samples.Wholesale.Sales.DraftOrders;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Queries;

public sealed partial class QueryTests
{
    [Fact]
    public async Task ResourceGrantsFilterDetailsTotalsAndPagesBeforeMaterialization()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await GrantAsync(provider, "reader", 20, DraftOrderAction.Read);
        await GrantAsync(provider, "reader", 30, DraftOrderAction.Read);
        await GrantAsync(provider, "reader", 30, DraftOrderAction.Edit);
        var probe = new QueryProbe();
        await using var observed = Provider(connection, probe);
        await using var read = SubjectScope(observed, Actor.Human(new("reader")));
        var queries = read.ServiceProvider.GetRequiredService<IDraftOrderQueries>();

        var first = await queries.SearchAsync(Filter, Token);
        Assert.Equal(2, first.TotalCount);
        var item = Assert.Single(first.Items);
        Assert.Equal(Id(20), item.Id);
        Assert.Equal(40, item.Total);
        Assert.Equal(2, item.LineCount);
        Assert.Equal(2, probe.Commands.Count);
        Assert.All(probe.Commands, sql => Assert.Contains("EXISTS", sql, StringComparison.Ordinal));
        Assert.All(
            probe.Commands,
            sql => Assert.Contains("draft_order_grants", sql, StringComparison.Ordinal)
        );
        var second = await queries.SearchAsync(Filter with { Offset = 1 }, Token);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal(Id(30), Assert.Single(second.Items).Id);
        var beyond = await queries.SearchAsync(Filter with { Offset = 2 }, Token);
        Assert.Equal(2, beyond.TotalCount);
        Assert.Empty(beyond.Items);
        Assert.Equal(2, (await queries.ReadAsync(Id(20), Token))!.Lines.Count);
        Assert.Null(await queries.ReadAsync(Id(40), Token));
        Assert.Null(await queries.ReadSummaryAsync(Id(40), Token));
        Assert.Null(await queries.ReadAsync(Id(999), Token));
        Assert.Empty(
            read.ServiceProvider.GetRequiredService<SalesDbContext>().ChangeTracker.Entries()
        );

        await using var other = SubjectScope(
            provider,
            Actor.Human(new("reader")),
            "wholesale-beta"
        );
        var otherQueries = other.ServiceProvider.GetRequiredService<IDraftOrderQueries>();
        Assert.Null(await otherQueries.ReadAsync(Id(20), Token));
        Assert.Equal(0, (await otherQueries.SearchAsync(new(), Token)).TotalCount);
    }

    [Fact]
    public async Task ReadGrantCannotEditOrSubmitAndEditGrantIncludesRead()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await GrantAsync(provider, "reader", 20, DraftOrderAction.Read);
        await using (var readOnly = SubjectScope(provider, Actor.Human(new("reader"))))
        {
            var database = readOnly.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var commands = readOnly.ServiceProvider.GetRequiredService<IDraftOrderCommands>();
            var checks = readOnly.ServiceProvider.GetRequiredService<IDraftOrderAuthorization>();
            Assert.True(await checks.IsAllowedAsync(Id(20), DraftOrderAction.Read, Token));
            Assert.False(await checks.IsAllowedAsync(Id(20), DraftOrderAction.Edit, Token));
            foreach (int suffix in new[] { 20, 40, 999 })
            {
                Assert.IsType<DraftOrderChangeResult.NotFound>(
                    await commands.ChangeLinesAsync(
                        new(Id(suffix), 1, [new DraftOrderLineChange.Change(Id(1), 4, 10)]),
                        Token
                    )
                );
                Assert.IsType<DraftOrderChangeResult.NotFound>(
                    await commands.SubmitAsync(new(Id(suffix), 1), Token)
                );
            }

            Assert.False(database.ChangeTracker.HasChanges());
            await transaction.CommitAsync(Token);
        }

        await GrantAsync(provider, "reader", 20, DraftOrderAction.Edit);
        await using (var editor = SubjectScope(provider, Actor.Human(new("reader"))))
        {
            var database = editor.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await editor
                    .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                    .ChangeLinesAsync(
                        new(Id(20), 1, [new DraftOrderLineChange.Change(Id(1), 4, 10)]),
                        Token
                    )
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using var inspect = CqrsComposition.CreateScope(provider);
        var current = await inspect
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadAsync(Id(20), Token);
        Assert.Equal(2, current!.Version);
        Assert.Equal(60, current.Total);
    }

    [Fact]
    public async Task SystemSubjectNeedsItsOwnGrantAndDoesNotInheritTheHumanInitiator()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await GrantAsync(provider, "same-key", 20, DraftOrderAction.Read);
        await using (
            var system = SubjectScope(
                provider,
                Actor.System(new("same-key")),
                initiator: Actor.Human(new("cqrs-demo-human"))
            )
        )
        {
            var page = await system
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .SearchAsync(new(), Token);
            Assert.Equal(0, page.TotalCount);
        }

        await GrantAsync(provider, "same-key", 40, DraftOrderAction.Read, ActorKind.System);
        await using var admitted = SubjectScope(provider, Actor.System(new("same-key")));
        var visible = await admitted
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .SearchAsync(new(), Token);
        Assert.Equal(Id(40), Assert.Single(visible.Items).Id);
        Assert.Equal(1, visible.TotalCount);
        var database = admitted.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            admitted
                .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                .ChangeLinesAsync(
                    new(Id(40), 1, [new DraftOrderLineChange.Change(Id(1), 2, 10)]),
                    Token
                )
        );
    }

    [Fact]
    public async Task RevocationIsReadAgainWithinTheSameImmutableOperation()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await GrantAsync(provider, "reader", 20, DraftOrderAction.Read);
        await using var read = SubjectScope(provider, Actor.Human(new("reader")));
        var queries = read.ServiceProvider.GetRequiredService<IDraftOrderQueries>();
        Assert.NotNull(await queries.ReadAsync(Id(20), Token));
        Assert.True(
            await read
                .ServiceProvider.GetRequiredService<IDraftOrderAuthorization>()
                .IsAllowedAsync(Id(20), DraftOrderAction.Read, Token)
        );
        await RevokeAsync(provider, "reader", 20);
        Assert.Null(await queries.ReadAsync(Id(20), Token));
        Assert.Equal(0, (await queries.SearchAsync(new(), Token)).TotalCount);
        Assert.Equal(
            "wholesale-alpha",
            read.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .Current.RequireTenant()
                .Value
        );
        Assert.Equal(
            "reader",
            read.ServiceProvider.GetRequiredService<IActorContextAccessor>().Current.Actor.Id!.Value
        );
    }

    [Fact]
    public async Task PermissionQueryFailureDoesNotBecomeACompleteEmptyPage()
    {
        string connection = await SeedAsync();
        var probe = new QueryProbe(() =>
            Task.FromException(new TimeoutException("permission query failed"))
        );
        await using var provider = Provider(connection, probe);
        await using var read = CqrsComposition.CreateScope(provider);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            read.ServiceProvider.GetRequiredService<IDraftOrderQueries>().SearchAsync(new(), Token)
        );
        Assert.IsType<TimeoutException>(failure.InnerException);
        Assert.Empty(
            read.ServiceProvider.GetRequiredService<SalesDbContext>().ChangeTracker.Entries()
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrantRevocationBetweenCountAndPageFollowsTheSelectedNativeSnapshot(
        bool repeatable
    )
    {
        string connection = await SeedAsync();
        await using var writer = Provider(connection);
        await GrantAsync(writer, "reader", 20, DraftOrderAction.Read);
        await GrantAsync(writer, "reader", 30, DraftOrderAction.Read);
        var probe = new QueryProbe(() => RevokeAsync(writer, "reader", 30));
        await using var provider = Provider(connection, probe);
        await using (var read = SubjectScope(provider, Actor.Human(new("reader"))))
        {
            var database = read.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                repeatable ? IsolationLevel.RepeatableRead : IsolationLevel.ReadCommitted,
                Token
            );
            var page = await read
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .SearchAsync(Filter with { Limit = 100 }, Token);
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(
                repeatable ? new[] { Id(20), Id(30) } : new[] { Id(20) },
                page.Items.Select(row => row.Id)
            );
            await transaction.CommitAsync(Token);
        }

        await using var fresh = SubjectScope(provider, Actor.Human(new("reader")));
        Assert.Equal(
            1,
            (
                await fresh
                    .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                    .SearchAsync(new(), Token)
            ).TotalCount
        );
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("anonymous")]
    public async Task PrivateQueriesRejectMissingOrAnonymousActorBeforeSql(string kind)
    {
        var probe = new QueryProbe();
        await using var provider = Provider("Host=localhost;Database=unused", probe);
        await using var operation = provider.CreateAsyncScope();
        operation
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new("wholesale-alpha")));
        if (kind == "anonymous")
            operation
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new(Actor.Anonymous));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .SearchAsync(new(), Token)
        );
        Assert.Empty(probe.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatorGrantAndDraftCommitOrRollbackTogether(bool commit)
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var create = SubjectScope(provider, Actor.Human(new("new-creator"))))
        {
            var database = create.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await create
                    .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                    .CreateAsync(CqrsDemoData.Draft(Id(70), 50), Token)
            );
            await database.SaveChangesAsync(Token);
            if (commit)
                await transaction.CommitAsync(Token);
            else
                await transaction.RollbackAsync(Token);
        }

        await using var inspect = SubjectScope(provider, Actor.Human(new("new-creator")));
        Assert.Equal(
            commit,
            await inspect
                .ServiceProvider.GetRequiredService<IDraftOrderAuthorization>()
                .IsAllowedAsync(Id(70), DraftOrderAction.Edit, Token)
        );
        var databaseState = inspect.ServiceProvider.GetRequiredService<SalesDbContext>();
        Assert.Equal(
            commit ? 1 : 0,
            await databaseState.DraftOrderGrants.CountAsync(row => row.OrderId == Id(70), Token)
        );
        Assert.Equal(
            commit ? 1 : 0,
            await databaseState.DraftOrders.CountAsync(row => row.Id == Id(70), Token)
        );
    }

    [Fact]
    public async Task MigrationPreservesExistingDraftsWithoutInventingGrants()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection);
        await using var operation = CqrsComposition.CreateScope(provider);
        var database = operation.ServiceProvider.GetRequiredService<SalesDbContext>();
        await database
            .GetService<IMigrator>()
            .MigrateAsync("20261014194424_AddRequiredDraftSubmission", Token);
        // Write the literal pre-B2 schema; the current EF model includes later delivery columns.
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sales.customer_profiles (id, organization_key, code, display_name, version)
            VALUES ('10000000-0000-0000-0000-000000000001', 'wholesale-alpha', 'DEMO-CUSTOMER', 'Alpha Customer', 1);
            INSERT INTO sales.customer_addresses (id, organization_key, customer_id, address_line)
            VALUES ('20000000-0000-0000-0000-000000000001', 'wholesale-alpha', '10000000-0000-0000-0000-000000000001', 'Alpha Street');
            """,
            Token
        );
        Guid oldOrder = Id(20);
        Guid oldLine = Id(1);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO sales.draft_orders (organization_key, id, customer_id, code, version, currency, total_limit, is_submitted)
            VALUES ('wholesale-alpha', {oldOrder}, {SalesDemoSeed.AlphaCustomerId}, 'LEGACY', 1, 'EUR', 100, false);
            INSERT INTO sales.draft_order_lines (organization_key, order_id, id, sku, quantity, unit_price)
            VALUES ('wholesale-alpha', {oldOrder}, {oldLine}, 'DEMO-NOTEBOOK', 1, 30);
            """,
            Token
        );
        await database.Database.MigrateAsync(Token);
        Assert.False(database.Database.HasPendingModelChanges());
        var preserved = await database
            .DraftOrders.AsNoTracking()
            .Include(row => row.Lines)
            .SingleAsync(Token);
        Assert.Equal("LEGACY", preserved.Code);
        Assert.Equal(1, preserved.Version);
        Assert.Equal(30, preserved.ToAggregate().ToView().Total);
        Assert.Empty(await database.DraftOrderGrants.ToArrayAsync(Token));
        Assert.Null(
            await operation
                .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
                .ReadAsync(Id(20), Token)
        );
    }

    [Fact]
    public async Task GrantRelationshipRequiresAnExistingDraftInTheSameOrganization()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using var beta = SubjectScope(provider, Actor.Human(new("reader")), "wholesale-beta");
        var database = beta.ServiceProvider.GetRequiredService<SalesDbContext>();
        database.DraftOrderGrants.Add(
            new()
            {
                OrganizationKey = "wholesale-beta",
                OrderId = Id(30), // Exists only in Alpha.
                SubjectKind = ActorKind.Human,
                SubjectKey = "reader",
                Permission = DraftOrderAction.Read,
            }
        );
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.SaveChangesAsync(Token)
        );
        var postgresFailure = Assert.IsType<Npgsql.PostgresException>(failure.InnerException);
        Assert.Equal(Npgsql.PostgresErrorCodes.ForeignKeyViolation, postgresFailure.SqlState);
    }

    private static AsyncServiceScope SubjectScope(
        ServiceProvider provider,
        Actor subject,
        string organization = "wholesale-alpha",
        Actor? initiator = null
    )
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new(organization)));
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new(subject, initiator));
        return scope;
    }

    private static async Task GrantAsync(
        ServiceProvider provider,
        string subject,
        int suffix,
        DraftOrderAction permission,
        ActorKind kind = ActorKind.Human
    )
    {
        await using var setup = CqrsComposition.CreateScope(provider);
        var database = setup.ServiceProvider.GetRequiredService<SalesDbContext>();
        database.DraftOrderGrants.Add(
            new()
            {
                OrganizationKey = "wholesale-alpha",
                OrderId = Id(suffix),
                SubjectKind = kind,
                SubjectKey = subject,
                Permission = permission,
            }
        );
        await database.SaveChangesAsync(Token);
    }

    private static async Task RevokeAsync(ServiceProvider provider, string subject, int suffix)
    {
        await using var setup = CqrsComposition.CreateScope(provider);
        await setup
            .ServiceProvider.GetRequiredService<SalesDbContext>()
            .DraftOrderGrants.Where(row => row.SubjectKey == subject && row.OrderId == Id(suffix))
            .ExecuteDeleteAsync(Token);
    }
}
