using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.MessagingWorkerDemo.Tests;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo.Tests;

public sealed partial class FeedTests
{
    [Fact]
    public async Task RepeatedPagesOldRepliesAndAmbiguousProducerRetryHaveOneDiagnosticEffect()
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        Guid subscription = Guid.NewGuid();
        await proof.InitializeJournalAsync(subscription, start, Token);
        await proof.WriteAsync(1, Token);
        Assert.IsType<ModulithFoundry.Samples.Wholesale.Sales.Contracts.ProfileChangeResult.Conflict>(
            await proof.WriteAsync(1, Token)
        );
        await proof.WriteAsync(2, Token);
        var old = await proof.PageAsync(start, Token, 1);
        var complete = await proof.PageAsync(start, Token);
        var duplicate = await proof.PageAsync(start, Token);
        Assert.Equal(complete.Changes, duplicate.Changes);
        await proof.RecordAsync(subscription, start, complete, Token);
        await proof.RecordAsync(subscription, start, duplicate, Token);
        await proof.RecordAsync(subscription, start, old, Token);
        Assert.Equal((complete.Next, 2L), await proof.JournalAsync(subscription, Token));
        var changed = complete with
        {
            Changes = Array.AsReadOnly(
                new[]
                {
                    complete.Changes[0] with
                    {
                        Profile = complete.Changes[0].Profile with
                        {
                            DisplayName = "Conflicting duplicate",
                        },
                    },
                    complete.Changes[1],
                }
            ),
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof.RecordAsync(subscription, start, changed, Token)
        );
        Assert.Equal((complete.Next, 2L), await proof.JournalAsync(subscription, Token));
    }

    [Fact]
    public async Task JournalFailureAfterSaveBeforeCommitRollsBackReceiptsAndCheckpointTogether()
    {
        var fault = new RejectJournalSave();
        await using var proof = await FeedProof.CreateAsync(
            postgres,
            Token,
            journalInterceptor: fault
        );
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        Guid subscription = Guid.NewGuid();
        await proof.InitializeJournalAsync(subscription, start, Token);
        await proof.WriteAsync(1, Token);
        var page = await proof.PageAsync(start, Token);
        fault.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proof.RecordAsync(subscription, start, page, Token)
        );
        Assert.Equal((start, 0L), await proof.JournalAsync(subscription, Token));
        fault.Enabled = false;
        await proof.RecordAsync(subscription, start, await proof.PageAsync(start, Token), Token);
        Assert.Equal((page.Next, 1L), await proof.JournalAsync(subscription, Token));
    }

    [Fact]
    public async Task CompetingReadersUseNativeCheckpointConcurrencyAndFreshRetry()
    {
        var pause = new ReadPause(sql =>
            sql.Contains("FROM feed_journal.subscriptions AS ", StringComparison.Ordinal)
        )
        {
            Enabled = false,
        };
        await using var proof = await FeedProof.CreateAsync(
            postgres,
            Token,
            journalInterceptor: pause
        );
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        Guid subscription = Guid.NewGuid();
        await proof.InitializeJournalAsync(subscription, start, Token);
        await proof.WriteAsync(1, Token);
        var page = await proof.PageAsync(start, Token);
        pause.Enabled = true;
        Task first = proof.RecordAsync(subscription, start, page, Token);
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await proof.RecordAsync(subscription, start, page, Token);
        pause.Release();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first);
        await proof.RecordAsync(subscription, start, page, Token);
        Assert.Equal((page.Next, 1L), await proof.JournalAsync(subscription, Token));
    }

    [Fact]
    public async Task StalePageCannotSkipAheadOfAnUnprocessedSuffix()
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        Guid subscription = Guid.NewGuid();
        await proof.InitializeJournalAsync(subscription, start, Token);
        await proof.WriteAsync(1, Token);
        await proof.WriteAsync(2, Token);
        await proof.WriteAsync(3, Token);
        var first = await proof.PageAsync(start, Token, 1);
        var complete = await proof.PageAsync(start, Token, 3);
        await proof.RecordAsync(subscription, start, first, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proof.RecordAsync(subscription, start, complete, Token)
        );
        Assert.Equal((first.Next, 1L), await proof.JournalAsync(subscription, Token));
        await proof.RecordAsync(
            subscription,
            first.Next,
            await proof.PageAsync(first.Next, Token),
            Token
        );
        Assert.Equal((complete.Next, 3L), await proof.JournalAsync(subscription, Token));
    }

    [Theory]
    [InlineData("--pause-before-save", "Page read before journal save.")]
    [InlineData("--pause-before-commit", "Journal saved before commit.")]
    public async Task ActualReaderProcessDiesBeforeAndAfterCommitThenResumesWithoutDuplicateEffects(
        string pauseArgument,
        string marker
    )
    {
        await using var proof = await FeedProof.CreateAsync(postgres, Token);
        var start = (await proof.BoundsAsync(Token)).EarliestAvailable;
        Guid subscription = Guid.NewGuid();
        await proof.InitializeJournalAsync(subscription, start, Token);
        await proof.WriteAsync(1, Token);
        await proof.WriteAsync(2, Token);
        await using (
            var reader = new ChildHost(
                typeof(FeedComposition).Assembly,
                proof.Environment,
                "read",
                FeedComposition.Alpha,
                subscription.ToString("D"),
                pauseArgument
            )
        )
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            await reader.WaitForOutputAsync(marker, deadline.Token);
            Assert.Equal((start, 0L), await proof.JournalAsync(subscription, Token));
            await reader.KillAsync(deadline.Token);
        }

        Assert.Equal((start, 0L), await proof.JournalAsync(subscription, Token));
        var page = await proof.PageAsync(start, Token);
        await using (
            var reader = new ChildHost(
                typeof(FeedComposition).Assembly,
                proof.Environment,
                "read",
                FeedComposition.Alpha,
                subscription.ToString("D"),
                "--pause-after-commit"
            )
        )
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            await reader.WaitForOutputAsync("Journal committed:", deadline.Token);
            await reader.KillAsync(deadline.Token);
        }

        Assert.Equal((page.Next, 2L), await proof.JournalAsync(subscription, Token));
        await proof.WriteAsync(3, Token);
        await using (
            var reader = new ChildHost(
                typeof(FeedComposition).Assembly,
                proof.Environment,
                "read",
                FeedComposition.Alpha,
                subscription.ToString("D")
            )
        )
            Assert.Equal(0, await reader.WaitForExitAsync(Token));

        Assert.Equal(
            ((await proof.BoundsAsync(Token)).Head, 3L),
            await proof.JournalAsync(subscription, Token)
        );
        await proof.PruneAsync((await proof.BoundsAsync(Token)).Head, Token);
        // An already checkpointed subscriber stays valid at the floor; a lagging one stops.
        Guid lagging = Guid.NewGuid();
        await proof.InitializeJournalAsync(lagging, start, Token);
        await using (
            var reader = new ChildHost(
                typeof(FeedComposition).Assembly,
                proof.Environment,
                "read",
                FeedComposition.Alpha,
                lagging.ToString("D")
            )
        )
        {
            Assert.NotEqual(0, await reader.WaitForExitAsync(Token));
            Assert.Contains("Cursor expired", reader.Output, StringComparison.Ordinal);
        }

        Assert.Equal((start, 0L), await proof.JournalAsync(lagging, Token));
    }
}
