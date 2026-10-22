using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Dispatch;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Worker.Directory;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Directory;

public sealed class PopulationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid First = Guid.Parse("80000000-0000-0000-0000-000000000001");
    private static readonly Guid Last = Guid.Parse("90000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task HistoricalPopulationAndOngoingChangesAreQueryableWithoutSales()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        // Finite legacy setup intentionally predates publication and proves bootstrap beyond replay.
        await proof.OperationAsync(
            DirectoryProof.Alpha,
            async services =>
            {
                var source = services.GetRequiredService<Sales.SalesDbContext>();
                Sales.SalesDemoSeed.Stage(source);
                await source.SaveChangesAsync(Token);
                return true;
            }
        );
        Assert.Equal(
            0,
            await DirectoryProof.CountAsync(
                proof.SourceConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proof.SearchAsync(DirectoryProof.Alpha, Token)
        );
        progress = await proof.ApplyAsync(progress, await proof.PageAsync(null, 100, Token), Token);
        Assert.True(progress.Ready);
        var profile = await proof.OperationAsync(
            DirectoryProof.Alpha,
            services =>
                services
                    .GetRequiredService<ICustomerProfiles>()
                    .ReadAsync(Sales.SalesDemoSeed.AlphaCustomerId, Token)
        );
        await proof.EditAsync(profile!, "New delivery", Token);
        await proof.DrainAsync(Token);
        // Receiver-only service graph: no source registration, source connection or source query.
        await using var receiver = new ServiceCollection()
            .AddScoped<Gloaming.Tenancy.ITenantContextAccessor>(_ =>
            {
                var tenant = new Gloaming.Tenancy.TenantContextAccessor();
                tenant.Initialize(
                    Gloaming.Tenancy.TenantContext.ForTenant(
                        new Gloaming.Tenancy.TenantId(DirectoryProof.Alpha)
                    )
                );
                return tenant;
            })
            .AddDispatchDirectory(proof.ReceiverConnection)
            .BuildServiceProvider();
        await using var scope = receiver.CreateAsyncScope();
        var rows = await scope
            .ServiceProvider.GetRequiredService<ICustomerDirectory>()
            .SearchAsync("New", Token);
        Assert.Equal("New delivery Street", Assert.Single(rows).AddressLine);
    }

    [Fact]
    public async Task ProducerRetainsUpdatesBeforeReceiverAdmissionAndRecoversWithoutLostCapture()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        await proof.RegisterAsync(First, "FIRST", DirectoryProof.Alpha, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => proof.DispatchAsync(Token));
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.SourceConnection,
                "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NULL",
                Token
            )
        );
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        await proof.ApplyAsync(progress, await proof.PageAsync(null, 100, Token), Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (await proof.DispatchAsync(deadline.Token) == OutboxDispatchResult.NoWork)
            await Task.Delay(20, deadline.Token);

        Assert.Equal(InboxProcessingResult.Processed, await proof.ProcessAsync(Token));
        Assert.Single(await proof.SearchAsync(DirectoryProof.Alpha, Token));
        Assert.Equal(
            0,
            await DirectoryProof.CountAsync(
                proof.SourceConnection,
                "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NULL",
                Token
            )
        );
    }

    [Fact]
    public async Task OlderCopyCannotOverwriteCapturedUpdateAndRestartResumesDurablePageProgress()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        var first = await proof.RegisterAsync(First, "FIRST", DirectoryProof.Alpha, Token);
        await proof.RegisterAsync(Last, "LAST", DirectoryProof.Alpha, Token);
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        var olderPage = await proof.PageAsync(null, 1, Token);
        Assert.False(olderPage.Complete);
        first = await proof.EditAsync(first, "Updated", Token);
        await proof.DrainAsync(Token);
        progress = await proof.ApplyAsync(progress, olderPage, Token);
        Assert.Equal(First, progress.After);
        Assert.False(progress.Ready);
        // New service provider simulates process loss; no page/session state remains in memory.
        await using var restarted = new ServiceCollection()
            .AddDeliveryDirectory(proof.SourceConnection, proof.ReceiverConnection)
            .BuildServiceProvider();
        Assert.True(
            await DirectoryComposition.CopyNextAsync(restarted, DirectoryProof.Alpha, Token)
        );
        Assert.Equal(
            progress.PopulationId,
            (await proof.BeginAsync(DirectoryProof.Alpha, Token)).PopulationId
        );
        var rows = await proof.SearchAsync(DirectoryProof.Alpha, Token);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Updated", rows.Single(row => row.CustomerId == first.CustomerId).DisplayName);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof.ApplyAsync(progress, olderPage, Token)
        );
    }

    [Fact]
    public async Task LateCommitAndNewKeyBehindCopyCursorAreCapturedAfterBaselineCompletes()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        var first = await proof.RegisterAsync(First, "FIRST", DirectoryProof.Alpha, Token);
        await proof.RegisterAsync(Last, "LAST", DirectoryProof.Alpha, Token);
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        progress = await proof.ApplyAsync(progress, await proof.PageAsync(null, 1, Token), Token);
        await using var blocker = new NpgsqlConnection(proof.SourceConnection);
        await blocker.OpenAsync(Token);
        await using var transaction = await blocker.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "LOCK TABLE sales.customer_addresses IN SHARE MODE",
                blocker,
                transaction
            )
        )
            await command.ExecuteNonQueryAsync(Token);

        var edit = proof.EditAsync(first, "Late commit", Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (
            await DirectoryProof.CountAsync(
                proof.SourceConnection,
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%UPDATE sales.customer_addresses%'",
                deadline.Token
            ) == 0
        )
            await Task.Delay(20, deadline.Token);

        progress = await proof.ApplyAsync(
            progress,
            await proof.PageAsync(progress.After, 100, Token),
            Token
        );
        Assert.True(progress.Ready);
        await transaction.CommitAsync(Token);
        await edit;
        var behind = Guid.Parse("10000000-0000-0000-0000-000000000099");
        await proof.RegisterAsync(behind, "BEHIND", DirectoryProof.Alpha, Token);
        await proof.DrainAsync(Token);
        var rows = await proof.SearchAsync(DirectoryProof.Alpha, Token);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Late commit", rows.Single(row => row.CustomerId == First).DisplayName);
        Assert.Contains(rows, row => row.CustomerId == behind);
    }

    [Fact]
    public async Task WithdrawalsBeforeAndDuringCopyFenceOldMessagesAndRelistingHasNewRevision()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        var first = await proof.RegisterAsync(First, "FIRST", DirectoryProof.Alpha, Token);
        first = await proof.ListedAsync(first, false, Token);
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        var page = await proof.PageAsync(null, 100, Token);
        Assert.False(Assert.Single(page.Entries).Listed);
        await proof.ApplyAsync(progress, page, Token);
        await proof.DrainAsync(Token);
        Assert.Empty(await proof.SearchAsync(DirectoryProof.Alpha, Token));
        first = await proof.ListedAsync(first, true, Token);
        await proof.DrainAsync(Token);
        Assert.Single(await proof.SearchAsync(DirectoryProof.Alpha, Token));
        var oldState = new CustomerDeliveryState(
            First,
            first.Version,
            true,
            first.Code,
            first.DisplayName,
            first.AddressLine
        );
        first = await proof.ListedAsync(first, false, Token);
        await proof.DrainAsync(Token);
        await proof.ReceiveAsync(Guid.NewGuid(), oldState, Token);
        await proof.DrainAsync(Token);
        Assert.Empty(await proof.SearchAsync(DirectoryProof.Alpha, Token));
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.customer_directory WHERE NOT \"Listed\"",
                Token
            )
        );
    }

    [Fact]
    public async Task WithdrawalDuringCopyCannotBeResurrectedByTheOlderExportPage()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        var first = await proof.RegisterAsync(First, "FIRST", DirectoryProof.Alpha, Token);
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        var older = await proof.PageAsync(null, 100, Token);
        await proof.ListedAsync(first, false, Token);
        await proof.DrainAsync(Token);
        Assert.True((await proof.ApplyAsync(progress, older, Token)).Ready);
        Assert.Empty(await proof.SearchAsync(DirectoryProof.Alpha, Token));
    }

    [Fact]
    public async Task AdmissionAndTenantIsolationHoldAcrossExportIntakeAndSearch()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        var alpha = await proof.RegisterAsync(First, "ALPHA", DirectoryProof.Alpha, Token);
        await proof.RegisterAsync(Last, "BETA", DirectoryProof.Beta, Token);
        var state = new CustomerDeliveryState(
            alpha.CustomerId,
            alpha.Version,
            true,
            alpha.Code,
            alpha.DisplayName,
            alpha.AddressLine
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proof.ReceiveAsync(Guid.NewGuid(), state, Token)
        );
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        var page = await proof.PageAsync(null, 100, Token);
        Assert.Equal(First, Assert.Single(page.Entries).CustomerId);
        await proof.ApplyAsync(progress, page, Token);
        await proof.BeginAsync(DirectoryProof.Beta, Token);
        await DirectoryComposition.CopyNextAsync(proof.Services, DirectoryProof.Beta, Token);
        await proof.DrainAsync(Token);
        Assert.Equal(
            "ALPHA",
            Assert.Single(await proof.SearchAsync(DirectoryProof.Alpha, Token)).Code
        );
        Assert.Equal(
            "BETA",
            Assert.Single(await proof.SearchAsync(DirectoryProof.Beta, Token)).Code
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof.ReceiveAsync(Guid.NewGuid(), state, Token, "unadmitted")
        );
        await Assert.ThrowsAsync<InvalidDataException>(() => proof.BeginAsync("unadmitted", Token));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof.ApplyAsync(progress, page with { Organization = DirectoryProof.Beta }, Token)
        );
    }

    [Fact]
    public async Task DuplicateReceiptsAndOutOfOrderStateDoNotRepeatEffectsAndConflictsFailVisibly()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        await proof.BeginAsync(DirectoryProof.Alpha, Token);
        var state = new CustomerDeliveryState(First, 2, true, "ONE", "New", "Street");
        Guid id = Guid.NewGuid();
        await proof.ReceiveAsync(id, state, Token);
        await proof.ReceiveAsync(id, state, Token);
        Assert.Equal(InboxProcessingResult.Processed, await proof.ProcessAsync(Token));
        Assert.Equal(InboxProcessingResult.NoWork, await proof.ProcessAsync(Token));
        await proof.ReceiveAsync(
            Guid.NewGuid(),
            state with
            {
                Revision = 1,
                DisplayName = "Older",
            },
            Token
        );
        Assert.Equal(InboxProcessingResult.Processed, await proof.ProcessAsync(Token));
        await proof.ReceiveAsync(Guid.NewGuid(), state with { DisplayName = "Conflict" }, Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => proof.ProcessAsync(Token));
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.customer_directory WHERE \"Revision\" = 2 AND \"DisplayName\" = 'New'",
                Token
            )
        );
        Assert.Equal(
            2,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.inbox_messages WHERE processed_at IS NOT NULL",
                Token
            )
        );
    }
}
