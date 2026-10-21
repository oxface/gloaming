using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo.Tests;

public sealed partial class SalesReplyTests
{
    [Fact(Timeout = 60_000)]
    public async Task DeadLetterStoreRespectsAdmittedTenantForInspectionAndNativeRetry()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var alpha = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        var beta = await proof.Reservations.StartAsync(
            SalesReplyProof.Request(),
            token,
            WorkflowProof.Beta
        );
        var alphaReply = await proof.ReplyAsync(alpha.OrderId, token);
        var betaReply = await proof.ReplyAsync(beta.OrderId, token, WorkflowProof.Beta);
        await proof.IntakeAsync(alphaReply, token);
        await proof.IntakeAsync(betaReply, token);
        await SeedDeadLettersAsync(proof, token);
        string role = QuoteRuntimeRole(proof);
        // Test operator authority is explicit; the processor's default role has neither grant.
        await proof.SqlAsync(
            $"GRANT UPDATE (redrive_count, last_redriven_at) ON sales.inbox_messages TO {role}",
            token
        );
        await using var services = proof.Services(configure: services =>
            services.AddPostgresInboxDeadLetters<SalesDbContext>()
        );
        await using var scope = services.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<SalesDbContext>>();
        const string subscription = StockReservationReplyAdmission.Subscription;
        var page = await letters.ListAsync(subscription, "inventory", new(1), token);
        Assert.Equal(alphaReply.MessageId, Assert.Single(page.Items).MessageId);
        Assert.Null(page.NextCursor);
        Assert.Null(await letters.GetAsync(subscription, "inventory", betaReply.MessageId, token));
        Assert.Equal(
            InboxRetryResult.NotApplied,
            await letters.RetryAsync(subscription, "inventory", betaReply.MessageId, 1, token)
        );
        var observed = (
            await letters.GetAsync(subscription, "inventory", alphaReply.MessageId, token)
        )!;
        Assert.Equal(
            InboxRetryResult.Retried,
            await letters.RetryAsync(
                subscription,
                "inventory",
                alphaReply.MessageId,
                observed.Revision,
                token
            )
        );
        Assert.Null(await letters.GetAsync(subscription, "inventory", alphaReply.MessageId, token));
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE tenant_key = 'wholesale-beta' AND quarantined_at IS NOT NULL AND revision = 1 AND redrive_count = 0",
                token
            )
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE tenant_key = 'wholesale-alpha' AND quarantined_at IS NULL AND revision = 2 AND redrive_count = 1",
                token
            )
        );
        await using var betaServices = proof.Services(
            WorkflowProof.Beta,
            services => services.AddPostgresInboxDeadLetters<SalesDbContext>()
        );
        await using var betaScope = betaServices.CreateAsyncScope();
        Assert.Equal(
            betaReply.MessageId,
            Assert
                .Single(
                    (
                        await betaScope
                            .ServiceProvider.GetRequiredService<IInboxDeadLetters<SalesDbContext>>()
                            .ListAsync(subscription, "inventory", new(), token)
                    ).Items
                )
                .MessageId
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task SelectOnlyCredentialCanInspectButCannotRetry()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var order = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        var reply = await proof.ReplyAsync(order.OrderId, token);
        await proof.IntakeAsync(reply, token);
        await SeedDeadLettersAsync(proof, token);
        string role = QuoteRuntimeRole(proof);
        await proof.SqlAsync(
            $"""
            REVOKE UPDATE (processed_at, available_at, revision, failure_count, failures_since_redrive,
                last_failed_at, last_failure_code, quarantined_at) ON sales.inbox_messages FROM {role}
            """,
            token
        );
        await using var services = proof.Services(configure: services =>
            services.AddPostgresInboxDeadLetters<SalesDbContext>()
        );
        await using var scope = services.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<SalesDbContext>>();
        const string subscription = StockReservationReplyAdmission.Subscription;
        Assert.Single((await letters.ListAsync(subscription, "inventory", new(), token)).Items);
        Assert.NotNull(await letters.GetAsync(subscription, "inventory", reply.MessageId, token));
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            letters.RetryAsync(subscription, "inventory", reply.MessageId, 1, token)
        );
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        Assert.Equal(
            1,
            (await letters.GetAsync(subscription, "inventory", reply.MessageId, token))!.Revision
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE revision = 1 AND redrive_count = 0 AND quarantined_at IS NOT NULL",
                token
            )
        );
    }

    private static Task SeedDeadLettersAsync(SalesReplyProof proof, CancellationToken token) =>
        proof.SqlAsync(
            """
            UPDATE sales.inbox_messages SET failure_count = 1, failures_since_redrive = 1, revision = 1,
                last_failed_at = clock_timestamp(), last_failure_code = 'operator-review', quarantined_at = clock_timestamp()
            """,
            token
        );

    private static string QuoteRuntimeRole(SalesReplyProof proof)
    {
        using var identifiers = new NpgsqlCommandBuilder();
        return identifiers.QuoteIdentifier(
            new NpgsqlConnectionStringBuilder(proof.Runtime).Username!
        );
    }
}
