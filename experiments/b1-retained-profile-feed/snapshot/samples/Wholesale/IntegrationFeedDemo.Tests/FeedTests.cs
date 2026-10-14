using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo.Tests;

public sealed partial class FeedTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NaiveSequenceMaxCursorLosesAnEarlierAllocatedLateCommit()
    {
        string database = await postgres.CreateDatabaseAsync(Token);
        await FeedProof.ExecuteAsync(
            database,
            "CREATE TABLE naive_feed (position bigserial PRIMARY KEY, value text NOT NULL)",
            Token
        );
        await using var a = new NpgsqlConnection(database);
        await using var b = new NpgsqlConnection(database);
        await a.OpenAsync(Token);
        await b.OpenAsync(Token);
        await using var first = await a.BeginTransactionAsync(Token);
        await using var second = await b.BeginTransactionAsync(Token);
        await using var insertA = new NpgsqlCommand(
            "INSERT INTO naive_feed(value) VALUES ('late') RETURNING position",
            a,
            first
        );
        long low = (long)(await insertA.ExecuteScalarAsync(Token))!;
        await using var insertB = new NpgsqlCommand(
            "INSERT INTO naive_feed(value) VALUES ('early') RETURNING position",
            b,
            second
        );
        long high = (long)(await insertB.ExecuteScalarAsync(Token))!;
        await second.CommitAsync(Token);
        Assert.True(low < high);
        Assert.Equal(
            high,
            await FeedProof.ScalarAsync(database, "SELECT MAX(position) FROM naive_feed", Token)
        );
        await first.CommitAsync(Token);
        Assert.Equal(
            2,
            await FeedProof.ScalarAsync(database, "SELECT count(*) FROM naive_feed", Token)
        );
        Assert.Equal(
            0,
            await FeedProof.ScalarAsync(
                database,
                $"SELECT count(*) FROM naive_feed WHERE position > {high}",
                Token
            )
        );
    }

    [Fact]
    public async Task AllocatedLateCommitBlocksDifferentCustomerUntilItsPositionIsSafe()
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        await using var barrier = await FeedBarrier.HoldAsync(
            proof.Source,
            "sales.customer_profile_changes",
            "INSERT",
            Token
        );
        Task<ProfileChangeResult> first = proof.WriteAsync(1, Token, "First");
        await barrier.WaitAsync("advisory", 1, Token);
        Task<ProfileChangeResult> second = proof.WriteAsync(1, Token, "Second", other: true);
        await barrier.WaitAsync("transactionid", 1, Token);
        Assert.False(second.IsCompleted);
        var beforeCommit = await proof.PageAsync(start, Token);
        Assert.Empty(beforeCommit.Changes);
        Assert.Equal(start, beforeCommit.Next);
        await barrier.ReleaseAsync(Token);
        Assert.IsType<ProfileChangeResult.Updated>(await first);
        Assert.IsType<ProfileChangeResult.Updated>(await second);
        var page = await proof.PageAsync(beforeCommit.Next, Token);
        Assert.Equal(
            ["First", "Second"],
            page.Changes.Select(change => change.Profile.DisplayName)
        );
        Assert.True(page.Changes[0].Position < page.Changes[1].Position);
        Assert.Equal(page.Bounds.Head, page.Next);
    }

    [Fact]
    public async Task TransactionStartedEarlierButAllocatedLaterAppearsAfterSavedCursor()
    {
        var pause = new TransactionPause();
        await using var proof = await FeedProof.CreateAsync(
            postgres,
            Token,
            sourceInterceptor: pause
        );
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        pause.Enabled = true;
        Task<ProfileChangeResult> early = proof.WriteAsync(
            1,
            Token,
            "Started earlier",
            other: true
        );
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.IsType<ProfileChangeResult.Updated>(
            await proof.WriteAsync(1, Token, "Committed first")
        );
        var captured = await proof.PageAsync(start, Token);
        Assert.Equal("Committed first", Assert.Single(captured.Changes).Profile.DisplayName);
        pause.Release();
        Assert.IsType<ProfileChangeResult.Updated>(await early);
        var late = await proof.PageAsync(captured.Next, Token);
        Assert.Equal("Started earlier", Assert.Single(late.Changes).Profile.DisplayName);
        Assert.True(late.Next.AfterPosition > captured.Next.AfterPosition);
    }

    [Fact]
    public async Task GapsPagingNewWritesAndTenantIsolationPreserveCommittedProgress()
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        Assert.IsType<ProfileChangeResult.Updated>(await proof.WriteAsync(1, Token));
        // PostgreSQL nextval survives rollback, unlike the committed feed head.
        await using (var scope = proof.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await database
                .Database.SqlQueryRaw<long>(
                    "SELECT nextval('sales.customer_profile_feed_positions') AS \"Value\""
                )
                .SingleAsync(Token);
            await transaction.RollbackAsync(Token);
        }

        Assert.IsType<ProfileChangeResult.Updated>(
            await proof.WriteAsync(1, Token, organization: FeedComposition.Beta)
        );
        Assert.IsType<ProfileChangeResult.Updated>(await proof.WriteAsync(2, Token));
        var first = await proof.PageAsync(start, Token, 1);
        Assert.True(first.HasMore);
        Assert.Equal(1, first.Next.AfterPosition);
        Assert.Equal(4, first.Bounds.Head.AfterPosition);
        Assert.IsType<ProfileChangeResult.Updated>(await proof.WriteAsync(3, Token));
        var middle = await proof.PageAsync(first.Next, Token, 1);
        Assert.Equal(4, Assert.Single(middle.Changes).Position);
        Assert.True(middle.HasMore);
        var last = await proof.PageAsync(middle.Next, Token, 1);
        Assert.Equal(4, Assert.Single(last.Changes).Profile.Version);
        Assert.False(last.HasMore);
        Assert.Equal(last.Bounds.Head, last.Next);
        var gap = await proof.PageAsync(new(start.Generation, 2), Token);
        Assert.Equal(new long[] { 4, 5 }, gap.Changes.Select(change => change.Position));
        Assert.All(
            gap.Changes,
            change => Assert.Equal(SalesDemoSeed.AlphaCustomerId, change.Profile.CustomerId)
        );
        Assert.IsType<CustomerProfileFeedRead.InvalidCursor>(
            await proof.ReadAsync(start, Token, organization: FeedComposition.Beta)
        );
        var empty = await proof.PageAsync(last.Next, Token);
        Assert.Empty(empty.Changes);
        Assert.Equal(last.Next, empty.Next);
        Assert.False(empty.HasMore);
    }

    [Theory]
    [InlineData("entry", "BEFORE INSERT", "sales.customer_profile_changes")]
    [InlineData("address", "BEFORE UPDATE", "sales.customer_addresses")]
    [InlineData("audit", "BEFORE INSERT", "sales.audit_entries")]
    [InlineData("head", "BEFORE UPDATE", "sales.customer_profile_feed_state")]
    [InlineData("commit", "AFTER INSERT", "sales.customer_profile_changes")]
    public async Task ProducerParticipantAndCommitFailuresRollbackBusinessAuditEntryAndHead(
        string failure,
        string operation,
        string table
    )
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        bool commit = failure == "commit";
        await FeedProof.ExecuteAsync(
            proof.Source,
            $"""
            CREATE FUNCTION public.reject_feed() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected feed participant failure'; END; $$;
            CREATE {(commit ? "CONSTRAINT " : "")}TRIGGER reject_feed {operation} ON {table}
            {(
                commit ? "DEFERRABLE INITIALLY DEFERRED" : ""
            )} FOR EACH ROW EXECUTE FUNCTION public.reject_feed();
            """,
            Token
        );
        await Assert.ThrowsAnyAsync<Exception>(() => proof.WriteAsync(1, Token));
        Assert.Equal(start, (await proof.BoundsAsync(Token)).Head);
        Assert.Empty((await proof.PageAsync(start, Token)).Changes);
        Assert.Equal(
            0,
            await FeedProof.ScalarAsync(
                proof.Source,
                "SELECT count(*) FROM sales.audit_entries",
                Token
            )
        );
        await using (var scope = proof.Scope())
        {
            var profile = await scope
                .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                .ReadAsync(SalesDemoSeed.AlphaCustomerId, Token);
            Assert.Equal("Alpha Customer", profile!.DisplayName);
            Assert.Equal("Alpha Street", profile.AddressLine);
            Assert.Equal(1, profile.Version);
        }

        await FeedProof.ExecuteAsync(proof.Source, $"DROP TRIGGER reject_feed ON {table}", Token);
        Assert.IsType<ProfileChangeResult.Updated>(await proof.WriteAsync(1, Token, "Recovered"));
        var recovered = await proof.PageAsync(start, Token);
        Assert.Equal("Recovered", Assert.Single(recovered.Changes).Profile.DisplayName);
        Assert.True(recovered.Next.AfterPosition > 1);
    }

    [Fact]
    public async Task CancellationAfterCustomerSaveAndAllocationRollsBackAndFreshEditRecovers()
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        await using var barrier = await FeedBarrier.HoldAsync(
            proof.Source,
            "sales.customer_profile_changes",
            "INSERT",
            Token
        );
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task<ProfileChangeResult> write = proof.WriteAsync(1, cancellation.Token);
        await barrier.WaitAsync("advisory", 1, Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.Equal(start, (await proof.BoundsAsync(Token)).Head);
        await barrier.ReleaseAsync(Token);
        Assert.IsType<ProfileChangeResult.Updated>(await proof.WriteAsync(1, Token));
        Assert.Equal(2, Assert.Single((await proof.PageAsync(start, Token)).Changes).Position);
    }

    [Fact]
    public async Task RetentionFloorGapBoundaryExpiryAndIdempotenceAreExplicit()
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        await proof.WriteAsync(1, Token);
        await FeedProof.ScalarAsync(
            proof.Source,
            "SELECT nextval('sales.customer_profile_feed_positions')",
            Token
        );
        await proof.WriteAsync(2, Token);
        await proof.PruneAsync(new(start.Generation, 2), Token);
        Assert.IsType<CustomerProfileFeedRead.CursorExpired>(await proof.ReadAsync(start, Token));
        var boundary = await proof.PageAsync(new(start.Generation, 2), Token);
        Assert.Equal(3, Assert.Single(boundary.Changes).Position);
        Assert.Equal(2, boundary.Bounds.EarliestAvailable.AfterPosition);
        await proof.PruneAsync(new(start.Generation, 2), Token);
        await proof.PruneAsync(new(start.Generation, 1), Token);
        Assert.Equal(2, (await proof.BoundsAsync(Token)).EarliestAvailable.AfterPosition);
        Assert.IsType<CustomerProfileFeedRead.InvalidCursor>(
            await proof.ReadAsync(new(start.Generation, 4), Token)
        );
        Assert.IsType<CustomerProfileFeedRead.InvalidCursor>(
            await proof.ReadAsync(new(Guid.NewGuid(), 2), Token)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            proof.PruneAsync(new(start.Generation, 4), Token)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            proof.PruneAsync(new(Guid.NewGuid(), 2), Token)
        );
        await proof.PruneAsync(boundary.Next, Token);
        var empty = await proof.PageAsync(boundary.Next, Token);
        Assert.Empty(empty.Changes);
        Assert.Equal(empty.Bounds.Head, empty.Bounds.EarliestAvailable);
        Assert.Equal(
            3,
            await FeedProof.ScalarAsync(
                proof.Source,
                "SELECT version FROM sales.customer_profiles WHERE organization_key = 'wholesale-alpha' AND code = 'DEMO-CUSTOMER'",
                Token
            )
        );
    }

    [Fact]
    public async Task RetentionFailureRestoresBothFloorAndDeletedPrefix()
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        await proof.WriteAsync(1, Token);
        var before = await proof.PageAsync(start, Token);
        await FeedProof.ExecuteAsync(
            proof.Source,
            """
            CREATE FUNCTION public.reject_floor() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected floor failure'; END; $$;
            CREATE TRIGGER reject_floor BEFORE UPDATE ON sales.customer_profile_feed_state
            FOR EACH ROW EXECUTE FUNCTION public.reject_floor();
            """,
            Token
        );
        await Assert.ThrowsAsync<PostgresException>(() => proof.PruneAsync(before.Next, Token));
        var after = await proof.PageAsync(start, Token);
        Assert.Equal(before.Bounds, after.Bounds);
        Assert.Equal(before.Changes, after.Changes);
    }

    [Fact]
    public async Task PageSnapshotSurvivesPruneBetweenBoundsAndEntriesThenNextCallExpires()
    {
        var pause = new ReadPause(sql =>
            sql.Contains("FROM sales.customer_profile_feed_state AS ", StringComparison.Ordinal)
            && sql.StartsWith("SELECT", StringComparison.Ordinal)
        );
        await using var proof = await FeedProof.CreateAsync(
            postgres,
            Token,
            sourceInterceptor: pause
        );
        // Obtain generation without consuming the pause on DescribeAsync.
        await using var scope = proof.Scope();
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        Guid generation = await database
            .Database.SqlQueryRaw<Guid>(
                "SELECT generation AS \"Value\" FROM sales.customer_profile_feed_state WHERE organization_key = 'wholesale-alpha'"
            )
            .SingleAsync(Token);
        var start = new CustomerProfileFeedCursor(generation, 0);
        await proof.WriteAsync(1, Token);
        await proof.WriteAsync(2, Token);
        Task<CustomerProfileFeedRead.Page> read = proof.PageAsync(start, Token, 1);
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await proof.PruneAsync(new(generation, 2), Token);
        pause.Release();
        var page = await read;
        Assert.Equal(1, Assert.Single(page.Changes).Position);
        Assert.Equal(0, page.Bounds.EarliestAvailable.AfterPosition);
        Assert.True(page.HasMore);
        Assert.IsType<CustomerProfileFeedRead.CursorExpired>(
            await proof.ReadAsync(page.Next, Token)
        );
    }

    [Fact]
    public async Task ActivationIsExplicitAndRepeatedSetupNeverResetsProgress()
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var before = await proof.BoundsAsync(Token);
        await proof.WriteAsync(1, Token);
        await proof.PruneAsync((await proof.BoundsAsync(Token)).Head, Token);
        var retained = await proof.BoundsAsync(Token);
        await FeedComposition.SetupAsync(proof.Services, Token);
        Assert.Equal(retained, await proof.BoundsAsync(Token));
        Assert.Equal(before.Head.Generation, retained.Head.Generation);
        Assert.Equal(
            2,
            await FeedProof.ScalarAsync(
                proof.Source,
                "SELECT version FROM sales.customer_profiles WHERE code = 'DEMO-CUSTOMER' AND organization_key = 'wholesale-alpha'",
                Token
            )
        );
        await FeedProof.ExecuteAsync(
            proof.Source,
            "DELETE FROM sales.customer_profile_feed_state WHERE organization_key = 'wholesale-alpha'",
            Token
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() => proof.BoundsAsync(Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => proof.WriteAsync(2, Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvalidPageSizesRejectBeforeReading(int size)
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            proof.ReadAsync((new(Guid.NewGuid(), 0)), Token, size)
        );
    }
}

internal sealed class TransactionPause : DbTransactionInterceptor
{
    private readonly TaskCompletionSource reached = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource release = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int used;
    internal bool Enabled { get; set; }
    internal Task Reached => reached.Task;

    internal void Release() => release.TrySetResult();

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        if (Enabled && Interlocked.CompareExchange(ref used, 1, 0) == 0)
        {
            reached.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}
