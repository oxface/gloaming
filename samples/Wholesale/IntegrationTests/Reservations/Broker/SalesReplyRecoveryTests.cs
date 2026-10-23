using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Samples.Wholesale.Worker.Broker;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed partial class SalesReplyTests
{
    [Fact(Timeout = 60_000)]
    public async Task RestrictedFailureAccountingQuarantinesOnlyAdmittedTenantWithoutOperatorRegistration()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var alpha = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        var beta = await proof.Reservations.StartAsync(
            SalesReplyProof.Request(),
            token,
            WorkflowProof.Beta
        );
        await proof.IntakeAsync(await proof.ReplyAsync(alpha.OrderId, token), token);
        await proof.IntakeAsync(
            await proof.ReplyAsync(beta.OrderId, token, WorkflowProof.Beta),
            token
        );
        await proof.SqlAsync(
            "ALTER TABLE sales.workflow_instances ADD CONSTRAINT reject_reply CHECK (false) NOT VALID",
            token
        );
        await using var services = proof.Services(configure: services =>
            services.AddInboxFailurePolicy<SalesDbContext, StopReplyPolicy>(
                StockReservationReplyAdmission.Subscription
            )
        );
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() =>
            SalesReplyProof.ProcessAsync(services, token)
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                """
                SELECT count(*) FROM sales.inbox_messages WHERE tenant_key = 'wholesale-alpha'
                    AND failure_count = 1 AND failures_since_redrive = 1 AND revision = 1
                    AND quarantined_at IS NOT NULL AND last_failure_code = 'reply-review' AND processed_at IS NULL
                """,
                token
            )
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                """
                SELECT count(*) FROM sales.inbox_messages WHERE tenant_key = 'wholesale-beta'
                    AND failure_count = 0 AND revision = 0 AND quarantined_at IS NULL AND processed_at IS NULL
                """,
                token
            )
        );
        Assert.Equal(
            StockReservationOrderStatus.AwaitingFirst,
            (await proof.Reservations.ReadAsync(alpha.OrderId, token))!.Status
        );
        Assert.Equal(
            2,
            await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
        );
        Assert.Equal(
            InboxProcessingResult.NoWork,
            await SalesReplyProof.ProcessAsync(services, token)
        );
        await using var scope = services.CreateAsyncScope();
        Assert.Null(scope.ServiceProvider.GetService<IInboxDeadLetters<SalesDbContext>>());
    }

    [Fact(Timeout = 60_000)]
    public async Task MissingRecoveryGrantRejectsStartupAndCannotCommitBusinessEffectsOrPartialAccounting()
    {
        var token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var order = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        await proof.IntakeAsync(await proof.ReplyAsync(order.OrderId, token), token);
        using var identifiers = new NpgsqlCommandBuilder();
        string role = identifiers.QuoteIdentifier(
            new NpgsqlConnectionStringBuilder(proof.Runtime).Username!
        );
        await proof.SqlAsync(
            $"REVOKE UPDATE (failure_count) ON sales.inbox_messages FROM {role}",
            token
        );
        await using (var worker = proof.StartWorker())
        {
            Assert.NotEqual(0, await worker.WaitForExitAsync(token));
            Assert.Contains("grants are unsafe", worker.Output, StringComparison.Ordinal);
        }

        await proof.SqlAsync(
            "ALTER TABLE sales.workflow_instances ADD CONSTRAINT reject_reply CHECK (false) NOT VALID",
            token
        );
        await using var services = proof.Services();
        var failure = await Assert.ThrowsAsync<InboxFailureAccountingException>(() =>
            SalesReplyProof.ProcessAsync(services, token)
        );
        Assert.IsType<Microsoft.EntityFrameworkCore.DbUpdateException>(failure.ProcessingFailure);
        Assert.Equal(
            PostgresErrorCodes.InsufficientPrivilege,
            Assert.IsType<PostgresException>(failure.AccountingFailure).SqlState
        );
        Assert.Equal(
            StockReservationOrderStatus.AwaitingFirst,
            (await proof.Reservations.ReadAsync(order.OrderId, token))!.Status
        );
        Assert.Equal(
            1,
            await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                """
                SELECT count(*) FROM sales.inbox_messages WHERE failure_count = 0 AND revision = 0
                    AND last_failed_at IS NULL AND quarantined_at IS NULL AND processed_at IS NULL
                """,
                token
            )
        );
    }

    public sealed class StopReplyPolicy : IInboxFailurePolicy<SalesDbContext>
    {
        public InboxFailureDecision Decide(InboxFailure failure) =>
            new InboxFailureDecision.DeadLetter("reply-review");
    }
}
