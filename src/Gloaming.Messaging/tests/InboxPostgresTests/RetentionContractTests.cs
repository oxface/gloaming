using System.Transactions;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed partial class RetentionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeduplicationAndContentEvidenceExpireOnlyOnDeletionAndOldDeliveryExecutesAgain(
        bool changedEnvelope
    )
    {
        string connection = await DatabaseAsync(postgres);
        Guid counter = Guid.NewGuid();
        await using (var setup = Context(connection))
        {
            setup.Add(
                new SharedState
                {
                    Id = counter,
                    Value = 0,
                    Version = 1,
                }
            );
            await setup.SaveChangesAsync(Token);
        }

        await using var provider = Provider(
            connection,
            new HandlerScenario
            {
                OnHandle = async (database, _, token) =>
                {
                    var state = await database
                        .Set<SharedState>()
                        .SingleAsync(row => row.Id == counter, token);
                    state.Value++;
                },
            }
        );
        var original = Message();
        var changed = Message(original.MessageId, "{\"value\":2}");
        Assert.Equal(InboxReceiveResult.Queued, await ReceiveAsync(provider, original));
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await using var read = Context(connection);
        var retained = await read.Set<InboxMessageRecord>().AsNoTracking().SingleAsync(Token);
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await ReceiveAsync(provider, original));
        await Assert.ThrowsAsync<InboxMessageConflictException>(() =>
            ReceiveAsync(provider, changed)
        );
        await SqlAsync(connection, "UPDATE receiver.inbox SET processed_at = '2020-01-01Z'");
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await ReceiveAsync(provider, original));
        await Assert.ThrowsAsync<InboxMessageConflictException>(() =>
            ReceiveAsync(provider, changed)
        );
        Assert.Equal(
            0,
            await read.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff.AddDays(-2),
                1,
                Token
            )
        );
        Assert.Equal(
            1,
            await read.DeleteProcessedPostgresInboxBatchAsync("render", "exports", Cutoff, 1, Token)
        );
        Assert.Equal(
            0,
            await read.DeleteProcessedPostgresInboxBatchAsync("render", "exports", Cutoff, 1, Token)
        );
        Assert.Equal(
            InboxReceiveResult.Queued,
            await ReceiveAsync(provider, changedEnvelope ? changed : original)
        );
        var recreated = await read.Set<InboxMessageRecord>().AsNoTracking().SingleAsync(Token);
        Assert.NotEqual(retained.ReceiptId, recreated.ReceiptId);
        Assert.True(recreated.ReceivedAt >= retained.ReceivedAt);
        Assert.Equal(0, recreated.Revision);
        Assert.Equal(0, recreated.FailureCount);
        Assert.Equal(0, recreated.RedriveCount);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(provider));
        Assert.Equal(2, (await read.Set<SharedState>().AsNoTracking().SingleAsync(Token)).Value);
    }

    [Fact]
    public async Task EqualProcessedTimesUseDatabaseUuidOrderAndRepeatedBatchesStayBounded()
    {
        string connection = await DatabaseAsync(postgres);
        await using var provider = Provider(connection);
        Guid[] ids =
        [
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
        ];
        foreach (Guid id in ids.Reverse())
            await ReceiveAsync(provider, Message(id));

        await SqlAsync(connection, "UPDATE receiver.inbox SET processed_at = '2020-01-01Z'");
        await using var database = Context(connection);
        Assert.Equal(
            2,
            await database.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff.ToOffset(TimeSpan.FromHours(3)),
                2,
                Token
            )
        );
        Assert.Equal(
            ids[2],
            (await database.Set<InboxMessageRecord>().AsNoTracking().SingleAsync(Token)).MessageId
        );
        Assert.Equal(
            1,
            await database.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                2,
                Token
            )
        );
        Assert.Equal(
            0,
            await database.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                2,
                Token
            )
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task InvalidBatchRejectsWithoutOpeningStorage(int batch)
    {
        await using var database = Context(
            "Host=127.0.0.1;Port=1;Database=absent;Username=unused;Timeout=1"
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                batch,
                Token
            )
        );
    }

    [Fact]
    public async Task CleanupRejectsFutureCutoffDirtyOrOwnedTransactionsAndCancellation()
    {
        string connection = await DatabaseAsync(postgres);
        await using var database = Context(connection);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            database.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                DateTimeOffset.MaxValue,
                1,
                Token
            )
        );
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            database.DeleteProcessedPostgresInboxBatchAsync(" ", "exports", Cutoff, 1, Token)
        );
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            database.DeleteProcessedPostgresInboxBatchAsync("render", "", Cutoff, 1, Token)
        );
        database.Add(new HandledItem { Id = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.DeleteProcessedPostgresInboxBatchAsync("render", "exports", Cutoff, 1, Token)
        );
        database.ChangeTracker.Clear();
        await using (var owned = await database.Database.BeginTransactionAsync(Token))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.DeleteProcessedPostgresInboxBatchAsync(
                    "render",
                    "exports",
                    Cutoff,
                    1,
                    Token
                )
            );

        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.DeleteProcessedPostgresInboxBatchAsync(
                    "render",
                    "exports",
                    Cutoff,
                    1,
                    Token
                )
            );

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.DeleteProcessedPostgresInboxBatchAsync(
                "render",
                "exports",
                Cutoff,
                1,
                cancelled.Token
            )
        );
    }
}
