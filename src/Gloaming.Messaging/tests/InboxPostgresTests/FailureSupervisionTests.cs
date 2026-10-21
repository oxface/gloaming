using System.Data.Common;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed class FailureSupervisionTests(PostgreSqlFixture postgres)
{
    [Theory]
    [InlineData("policy")]
    [InlineData("sql")]
    public async Task AccountingFaultEndsWorkerAfterOneAttemptAndRollsBackExecutedEffects(
        string fault
    )
    {
        string connection = await DatabaseAsync(postgres);
        if (fault == "sql")
            await SqlAsync(
                connection,
                "ALTER TABLE receiver.inbox ADD CONSTRAINT reject_accounting CHECK (failure_count = 0)"
            );

        var processingFailure = new IOException("handler failed after saving");
        var handler = new HandlerScenario
        {
            OnHandle = async (database, message, token) =>
            {
                database.Add(new HandledItem { Id = message.MessageId, Value = 7 });
                new EfOutbox<InboxConsumer>(database).Enqueue(
                    OutgoingMessage.FromPayload(
                        Guid.NewGuid(),
                        "replies",
                        "handled",
                        1,
                        new { message.MessageId },
                        new JsonSerializerOptions()
                    )
                );
                await database.SaveChangesAsync(token);
                throw processingFailure;
            },
        };
        var services = Services(connection, handler);
        if (fault == "policy")
            services.AddInboxFailurePolicy<InboxConsumer, ThrowingPolicy>("render");

        services.AddInboxWorker<InboxConsumer>(
            "render",
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
        );
        await using var provider = services.BuildServiceProvider();
        var original = Message();
        await ReceiveAsync(provider, original);
        var worker = Assert.IsAssignableFrom<BackgroundService>(
            Assert.Single(provider.GetServices<IHostedService>())
        );
        await worker.StartAsync(Token);
        try
        {
            var failure = await Assert.ThrowsAsync<InboxFailureAccountingException>(() =>
                worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Token)
            );
            Assert.Equal("render", failure.SubscriptionKey);
            Assert.Equal(original.ProducerKey, failure.ProducerKey);
            Assert.Equal(original.MessageId, failure.MessageId);
            Assert.Same(processingFailure, failure.ProcessingFailure);
            if (fault == "sql")
                Assert.Equal(
                    PostgresErrorCodes.CheckViolation,
                    Assert.IsType<PostgresException>(failure.AccountingFailure).SqlState
                );
            else
                Assert.IsType<InvalidOperationException>(failure.AccountingFailure);
        }
        finally
        {
            await worker.StopAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
        }

        Assert.Single(handler.Contexts);
        await using var read = Context(connection);
        Assert.Empty(await read.Set<HandledItem>().ToArrayAsync(Token));
        Assert.Empty(await read.Set<OutboxMessageRecord>().ToArrayAsync(Token));
        var row = await read.Set<InboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(0, row.FailureCount);
        Assert.Equal(0, row.Revision);
        Assert.Null(row.ProcessedAt);
        Assert.Null(row.QuarantinedAt);
        Assert.Equal(original.MessageId, row.MessageId);
        Assert.True(JsonElement.DeepEquals(original.Payload, row.Payload));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringAccountingPreservesCallerErrorsAndLetsWorkerStop(
        bool worker
    )
    {
        string connection = await DatabaseAsync(postgres);
        var processingFailure = new IOException("handler failed");
        var handler = new HandlerScenario { OnHandle = (_, _, _) => throw processingFailure };
        var rollback = new BlockingRollback();
        var services = Services(connection, handler);
        services.AddDbContext<InboxConsumer>(options => options.AddInterceptors(rollback));
        if (worker)
            services.AddInboxWorker<InboxConsumer>(
                "render",
                new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
            );

        await using var provider = services.BuildServiceProvider();
        var original = Message();
        await ReceiveAsync(provider, original);
        if (worker)
        {
            var hosted = Assert.IsAssignableFrom<BackgroundService>(
                Assert.Single(provider.GetServices<IHostedService>())
            );
            await hosted.StartAsync(Token);
            try
            {
                await rollback.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            }
            finally
            {
                await hosted.StopAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
            }

            Assert.True(hosted.ExecuteTask!.IsCompletedSuccessfully);
        }
        else
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var attempt = ProcessAsync(provider, cancellation.Token);
            try
            {
                await rollback.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            }
            finally
            {
                await cancellation.CancelAsync();
            }

            var failure = await Assert.ThrowsAsync<InboxFailureAccountingException>(() =>
                attempt.WaitAsync(TimeSpan.FromSeconds(10), Token)
            );
            Assert.Same(processingFailure, failure.ProcessingFailure);
            Assert.IsAssignableFrom<OperationCanceledException>(failure.AccountingFailure);
            Assert.Equal(original.MessageId, failure.MessageId);
        }

        Assert.Single(handler.Contexts);
        await using var read = Context(connection);
        var row = await read.Set<InboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(0, row.FailureCount);
        Assert.Equal(0, row.Revision);
        Assert.Null(row.ProcessedAt);
    }

    public sealed class ThrowingPolicy : IInboxFailurePolicy<InboxConsumer>
    {
        public InboxFailureDecision Decide(InboxFailure failure) =>
            throw new InvalidOperationException("classification failed");
    }

    private sealed class BlockingRollback : DbTransactionInterceptor
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> RollingBackToSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return result;
        }
    }
}
