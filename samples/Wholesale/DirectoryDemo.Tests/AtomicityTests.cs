using Gloaming.Messaging.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.DirectoryDemo.Tests;

public sealed class AtomicityTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid Customer = Guid.Parse("80000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task NativeConcurrentCopyAndInboxUpdateSerializeWithoutStaleOverwrite()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        var profile = await proof.RegisterAsync(Customer, "FIRST", DirectoryProof.Alpha, Token);
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        var page = await proof.PageAsync(null, 100, Token);
        var newer = new CustomerDeliveryState(
            Customer,
            2,
            true,
            profile.Code,
            "Updated",
            "Updated Street"
        );
        await proof.ReceiveAsync(Guid.NewGuid(), newer, Token);
        await using var blocker = new NpgsqlConnection(proof.ReceiverConnection);
        await blocker.OpenAsync(Token);
        await using var transaction = await blocker.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "SELECT * FROM dispatch.directory_populations FOR UPDATE",
                blocker,
                transaction
            )
        )
            await command.ExecuteNonQueryAsync(Token);
        var apply = proof.ApplyAsync(progress, page, Token);
        var process = proof.ProcessAsync(Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%directory_populations%'",
                deadline.Token
            ) < 2
        )
            await Task.Delay(20, deadline.Token);
        await transaction.CommitAsync(Token);
        await apply;
        Assert.Equal(InboxProcessingResult.Processed, await process);
        Assert.Equal(
            "Updated",
            Assert.Single(await proof.SearchAsync(DirectoryProof.Alpha, Token)).DisplayName
        );
    }

    [Fact]
    public async Task CopyFailureRollsBackRowsAndCheckpointAndRetryUsesSameProgress()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        await proof.RegisterAsync(Customer, "FIRST", DirectoryProof.Alpha, Token);
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        var page = await proof.PageAsync(null, 100, Token);
        await DirectoryProof.SqlAsync(
            proof.ReceiverConnection,
            """
            CREATE FUNCTION dispatch.reject_progress() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'proof progress failure'; END $$;
            CREATE TRIGGER reject_progress BEFORE UPDATE ON dispatch.directory_populations
            FOR EACH ROW EXECUTE FUNCTION dispatch.reject_progress();
            """,
            Token
        );
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() =>
            proof.ApplyAsync(progress, page, Token)
        );
        Assert.Equal(
            0,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.customer_directory",
                Token
            )
        );
        Assert.Equal(progress, await proof.BeginAsync(DirectoryProof.Alpha, Token));
        await DirectoryProof.SqlAsync(
            proof.ReceiverConnection,
            "DROP TRIGGER reject_progress ON dispatch.directory_populations",
            Token
        );
        Assert.True((await proof.ApplyAsync(progress, page, Token)).Ready);
        Assert.Single(await proof.SearchAsync(DirectoryProof.Alpha, Token));
    }

    [Fact]
    public async Task InboxCompletionFailureRollsBackAppliedStateAndRetainedDeliveryRetries()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        await proof.BeginAsync(DirectoryProof.Alpha, Token);
        await proof.ReceiveAsync(
            Guid.NewGuid(),
            new(Customer, 1, true, "FIRST", "First", "Street"),
            Token
        );
        await DirectoryProof.SqlAsync(
            proof.ReceiverConnection,
            """
            CREATE FUNCTION dispatch.reject_completion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.processed_at IS NOT NULL THEN RAISE EXCEPTION 'proof completion failure'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER reject_completion BEFORE UPDATE ON dispatch.inbox_messages
            FOR EACH ROW EXECUTE FUNCTION dispatch.reject_completion();
            """,
            Token
        );
        await Assert.ThrowsAsync<PostgresException>(() => proof.ProcessAsync(Token));
        Assert.Equal(
            0,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.customer_directory",
                Token
            )
        );
        Assert.Equal(
            0,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.inbox_messages WHERE processed_at IS NOT NULL",
                Token
            )
        );
        await DirectoryProof.SqlAsync(
            proof.ReceiverConnection,
            "DROP TRIGGER reject_completion ON dispatch.inbox_messages",
            Token
        );
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (await proof.ProcessAsync(deadline.Token) == InboxProcessingResult.NoWork)
            await Task.Delay(20, deadline.Token);

        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.customer_directory",
                Token
            )
        );
    }

    [Fact]
    public async Task RejectedSourceSaveCannotCommitProfileWithoutItsPublicUpdate()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        var profile = await proof.RegisterAsync(Customer, "FIRST", DirectoryProof.Alpha, Token);
        await DirectoryProof.SqlAsync(
            proof.SourceConnection,
            """
            CREATE FUNCTION sales.reject_publication() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'proof publication failure'; END $$;
            CREATE TRIGGER reject_publication BEFORE INSERT ON sales.outbox_messages
            FOR EACH ROW EXECUTE FUNCTION sales.reject_publication();
            """,
            Token
        );
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() =>
            proof.EditAsync(profile, "Rejected", Token)
        );
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.SourceConnection,
                "SELECT count(*) FROM sales.customer_profiles WHERE version = 1 AND display_name = 'FIRST Customer'",
                Token
            )
        );
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.SourceConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
    }
}
