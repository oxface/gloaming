using System.Data.Common;
using System.Text.Json;
using System.Transactions;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed class FailureRecoveryTests(PostgreSqlFixture postgres)
{
    public sealed class FailureScenario
    {
        public Func<InboxFailure, InboxFailureDecision> Decide { get; set; } =
            failure =>
                failure.FailuresSinceRedrive < 2
                    ? new InboxFailureDecision.RetryAfter(TimeSpan.FromSeconds(10), "temporary")
                    : new InboxFailureDecision.DeadLetter("review");
    }

    public sealed class Policy(FailureScenario scenario) : IInboxFailurePolicy<InboxConsumer>
    {
        public InboxFailureDecision Decide(InboxFailure failure) => scenario.Decide(failure);
    }

    private static ServiceProvider RecoveryProvider(
        string connection,
        HandlerScenario handler,
        FailureScenario? policy = null,
        DbTransactionInterceptor? interceptor = null
    )
    {
        var services = Services(connection, handler);
        services.AddSingleton(policy ?? new FailureScenario());
        services.AddInboxFailurePolicy<InboxConsumer, Policy>("render");
        services.AddPostgresInboxDeadLetters<InboxConsumer>();
        if (interceptor is not null)
            services.AddDbContext<InboxConsumer>(options => options.AddInterceptors(interceptor));

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private static async Task<InboxRetryResult> RetryAsync(
        ServiceProvider provider,
        IncomingMessage message,
        Guid receiptId,
        long revision
    )
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>()
            .RetryAsync(
                "render",
                message.ProducerKey,
                message.MessageId,
                receiptId,
                revision,
                Token
            );
    }

    private static async Task<InboxMessageRecord> ReadAsync(string connection, Guid messageId)
    {
        await using var database = Context(connection);
        return await database
            .Set<InboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(row => row.MessageId == messageId, Token);
    }

    private static Task FailAfterSaveAsync(
        InboxConsumer database,
        IncomingMessage message,
        CancellationToken token
    ) => SaveThenFailAsync(database, message, token);

    private static async Task SaveThenFailAsync(
        InboxConsumer database,
        IncomingMessage message,
        CancellationToken token
    )
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
        throw new IOException("private handler details must not become a durable code");
    }

    [Fact]
    public async Task CountsSurviveFreshProvidersAndRepeatedRedrivePreservesIdentityAndDeduplication()
    {
        string connection = await DatabaseAsync(postgres);
        var original = Message();
        var message = new IncomingMessage(
            original.MessageId,
            original.ProducerKey,
            original.MessageName,
            original.SchemaVersion,
            original.Payload,
            original.TenantKey,
            original.CorrelationId,
            original.CausationId,
            "00-12345678901234567890123456789012-1234567890123456-01",
            "proof=retained"
        );
        var handler = new HandlerScenario { OnHandle = FailAfterSaveAsync };
        await using (var first = RecoveryProvider(connection, handler))
        {
            await ReceiveAsync(first, message);
            await Assert.ThrowsAsync<IOException>(() => ProcessAsync(first));
            var row = await ReadAsync(connection, message.MessageId);
            Assert.Equal(1, row.FailureCount);
            Assert.Null(row.DeadLetteredAt);
            Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(first));
        }

        await using var restarted = RecoveryProvider(connection, handler);
        await ReadyAsync(connection);
        await Assert.ThrowsAsync<IOException>(() => ProcessAsync(restarted));
        var stopped = await ReadAsync(connection, message.MessageId);
        Assert.Equal(2, stopped.FailureCount);
        Assert.Equal(2, stopped.FailuresSinceRedrive);
        Assert.Equal("review", stopped.LastFailureCode);
        Assert.NotNull(stopped.DeadLetteredAt);
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(restarted));
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await ReceiveAsync(restarted, message));
        await Assert.ThrowsAsync<InboxMessageConflictException>(() =>
            ReceiveAsync(restarted, Message(message.MessageId, "{\"changed\":true}"))
        );
        Assert.Equal(
            InboxRetryResult.Retried,
            await RetryAsync(restarted, message, stopped.ReceiptId, stopped.Revision)
        );
        Assert.Equal(
            InboxRetryResult.NotApplied,
            await RetryAsync(restarted, message, stopped.ReceiptId, stopped.Revision)
        );
        var retried = await ReadAsync(connection, message.MessageId);
        Assert.Null(retried.DeadLetteredAt);
        Assert.Equal(0, retried.FailuresSinceRedrive);
        Assert.Equal(2, retried.FailureCount);
        Assert.Equal(1, retried.RedriveCount);
        Assert.NotNull(retried.LastRedrivenAt);

        // A different consumer decision after deployment can stop immediately; stale retry stays stale.
        var stop = new FailureScenario
        {
            Decide = _ => new InboxFailureDecision.DeadLetter("permanent"),
        };
        await using var changed = RecoveryProvider(connection, handler, stop);
        await Assert.ThrowsAsync<IOException>(() => ProcessAsync(changed));
        var again = await ReadAsync(connection, message.MessageId);
        Assert.Equal(3, again.FailureCount);
        Assert.Equal(1, again.FailuresSinceRedrive);
        Assert.Equal(
            InboxRetryResult.NotApplied,
            await RetryAsync(changed, message, stopped.ReceiptId, stopped.Revision)
        );
        Assert.Equal(
            InboxRetryResult.Retried,
            await RetryAsync(changed, message, again.ReceiptId, again.Revision)
        );
        handler.OnHandle = null;
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(changed));
        var completed = await ReadAsync(connection, message.MessageId);
        Assert.Equal(6, completed.Revision);
        Assert.Equal(3, completed.FailureCount);
        Assert.Equal(0, completed.FailuresSinceRedrive);
        Assert.Equal(2, completed.RedriveCount);
        Assert.NotNull(completed.ProcessedAt);
        Assert.Equal("permanent", completed.LastFailureCode);
        Assert.Equal(stopped.ReceivedAt, completed.ReceivedAt);
        Assert.True(JsonElement.DeepEquals(message.Payload, completed.Payload));
        Assert.Equal(message.MessageName, completed.MessageName);
        Assert.Equal(message.SchemaVersion, completed.SchemaVersion);
        Assert.Equal(message.TenantKey, completed.TenantKey);
        Assert.Equal(message.CorrelationId, completed.CorrelationId);
        Assert.Equal(message.CausationId, completed.CausationId);
        Assert.Equal(message.TraceParent, completed.TraceParent);
        Assert.Equal(message.TraceState, completed.TraceState);
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await ReceiveAsync(changed, message));
        Assert.Equal(
            InboxRetryResult.NotApplied,
            await RetryAsync(changed, message, completed.ReceiptId, completed.Revision)
        );
        await using var read = Context(connection);
        Assert.Single(await read.Set<HandledItem>().ToArrayAsync(Token));
        Assert.Empty(await read.Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }

    [Fact]
    public async Task RollbackRetainsDeliveryLockThroughAccountingWhileOtherDeliveriesProceed()
    {
        string connection = await DatabaseAsync(postgres);
        var message = Message();
        var handler = new HandlerScenario { OnHandle = FailAfterSaveAsync };
        var policy = new FailureScenario
        {
            Decide = _ => new InboxFailureDecision.DeadLetter("review"),
        };
        await using var provider = RecoveryProvider(connection, handler, policy);
        await ReceiveAsync(provider, message);
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync(Token);
        await using (
            var install = new NpgsqlCommand(
                """
                SELECT pg_advisory_lock(180021);
                CREATE FUNCTION receiver.failure_barrier() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN PERFORM pg_advisory_xact_lock(180021); RETURN NEW; END; $$;
                CREATE TRIGGER failure_barrier BEFORE UPDATE ON receiver.inbox
                    FOR EACH ROW WHEN (NEW.failure_count > OLD.failure_count) EXECUTE FUNCTION receiver.failure_barrier();
                """,
                barrier
            )
        )
            await install.ExecuteNonQueryAsync(Token);

        var pending = ProcessAsync(provider);
        try
        {
            await WaitAsync(async () =>
            {
                await using var observer = Context(connection);
                return await observer
                        .Database.SqlQueryRaw<long>(
                            """
                            SELECT count(*) AS "Value" FROM pg_locks l JOIN pg_stat_activity a ON a.pid = l.pid
                            WHERE a.datname = current_database() AND l.locktype = 'advisory' AND NOT l.granted
                            """
                        )
                        .SingleAsync(Token) == 1;
            });
            await using var read = Context(connection);
            Assert.Empty(await read.Set<HandledItem>().ToArrayAsync(Token));
            Assert.Empty(await read.Set<OutboxMessageRecord>().ToArrayAsync(Token));
            Assert.Equal(0, (await ReadAsync(connection, message.MessageId)).FailureCount);
            await using var inspection = provider.CreateAsyncScope();
            var deadLetters = inspection.ServiceProvider.GetRequiredService<
                IInboxDeadLetters<InboxConsumer>
            >();
            Assert.Null(
                await deadLetters
                    .GetAsync("render", "exports", message.MessageId, Token)
                    .WaitAsync(TimeSpan.FromSeconds(5), Token)
            );
            Assert.Empty(
                (
                    await deadLetters
                        .ListAsync("render", "exports", new(), Token)
                        .WaitAsync(TimeSpan.FromSeconds(5), Token)
                ).Items
            );
            await using var lockProbe = Context(connection);
            var blocked = await Assert.ThrowsAsync<PostgresException>(() =>
                lockProbe.Database.ExecuteSqlRawAsync(
                    "SELECT 1 FROM receiver.inbox FOR UPDATE NOWAIT",
                    Token
                )
            );
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, blocked.SqlState);
            Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(provider));
            // A currently nonquarantined row is inapplicable; no authority to interrupt its processor.
            Assert.Equal(
                InboxRetryResult.NotApplied,
                await RetryAsync(
                    provider,
                    message,
                    (await ReadAsync(connection, message.MessageId)).ReceiptId,
                    0
                )
            );
            handler.OnHandle = null;
            var unrelated = Message();
            await ReceiveAsync(provider, unrelated);
            Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
            Assert.NotNull((await ReadAsync(connection, unrelated.MessageId)).ProcessedAt);
        }
        finally
        {
            await using var release = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(180021)",
                barrier
            );
            await release.ExecuteNonQueryAsync(Token);
        }

        await Assert.ThrowsAsync<IOException>(() => pending);
        Assert.NotNull((await ReadAsync(connection, message.MessageId)).DeadLetteredAt);
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(provider));
    }

    [Fact]
    public async Task CompetingOperatorsRecheckRevisionAfterActualDatabaseWait()
    {
        string connection = await DatabaseAsync(postgres);
        var message = Message();
        await using var provider = RecoveryProvider(
            connection,
            new HandlerScenario { OnHandle = FailAfterSaveAsync },
            new FailureScenario { Decide = _ => new InboxFailureDecision.DeadLetter("review") }
        );
        await ReceiveAsync(provider, message);
        await Assert.ThrowsAsync<IOException>(() => ProcessAsync(provider));
        var stopped = await ReadAsync(connection, message.MessageId);
        await using var owner = Context(connection);
        await using var transaction = await owner.Database.BeginTransactionAsync(Token);
        await owner.Database.ExecuteSqlRawAsync("SELECT 1 FROM receiver.inbox FOR UPDATE", Token);
        var first = RetryAsync(provider, message, stopped.ReceiptId, stopped.Revision);
        var second = RetryAsync(provider, message, stopped.ReceiptId, stopped.Revision);
        await WaitAsync(async () =>
        {
            await using var observer = Context(connection);
            return await observer
                    .Database.SqlQueryRaw<long>(
                        """
                        SELECT count(*) AS "Value" FROM pg_stat_activity
                        WHERE datname = current_database() AND wait_event_type = 'Lock'
                        """
                    )
                    .SingleAsync(Token) == 2;
        });
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(provider));
        await transaction.CommitAsync(Token);
        var results = await Task.WhenAll(first, second);
        Assert.Equal(1, results.Count(result => result == InboxRetryResult.Retried));
        Assert.Equal(1, results.Count(result => result == InboxRetryResult.NotApplied));
        var retried = await ReadAsync(connection, message.MessageId);
        Assert.Equal(stopped.Revision + 1, retried.Revision);
        Assert.Equal(1, retried.RedriveCount);
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("policy")]
    [InlineData("invalid")]
    public async Task FailedAccountingPreservesErrorsAndRollsBackAllProposals(string fault)
    {
        string connection = await DatabaseAsync(postgres);
        if (fault == "sql")
            await SqlAsync(
                connection,
                "ALTER TABLE receiver.inbox ADD CONSTRAINT reject_accounting CHECK (failure_count = 0)"
            );
        if (fault == "commit")
            await SqlAsync(
                connection,
                """
                CREATE FUNCTION receiver.reject_accounting_commit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'accounting commit failed'; END; $$;
                CREATE CONSTRAINT TRIGGER reject_accounting_commit AFTER UPDATE ON receiver.inbox
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION receiver.reject_accounting_commit();
                """
            );

        var policy = new FailureScenario();
        if (fault == "policy")
            policy.Decide = _ => throw new InvalidOperationException("classification failed");
        if (fault == "invalid")
            policy.Decide = _ => new InboxFailureDecision.RetryAfter(TimeSpan.Zero, "invalid");

        await using var provider = RecoveryProvider(
            connection,
            new HandlerScenario { OnHandle = FailAfterSaveAsync },
            policy,
            fault == "rollback" ? new RollbackFailure() : null
        );
        var message = Message();
        await ReceiveAsync(provider, message);
        var failure = await Assert.ThrowsAsync<InboxFailureAccountingException>(() =>
            ProcessAsync(provider)
        );
        Assert.Equal("render", failure.SubscriptionKey);
        Assert.Equal(message.ProducerKey, failure.ProducerKey);
        Assert.Equal(message.MessageId, failure.MessageId);
        Assert.IsType<IOException>(failure.ProcessingFailure);
        Assert.Same(failure.ProcessingFailure, failure.InnerExceptions[0]);
        Assert.Same(failure.AccountingFailure, failure.InnerExceptions[1]);
        Assert.Equal(2, failure.InnerExceptions.Count);
        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(0, row.FailureCount);
        Assert.Equal(0, row.Revision);
        Assert.Null(row.DeadLetteredAt);
        Assert.Null(row.ProcessedAt);
        await using var read = Context(connection);
        Assert.Empty(await read.Set<HandledItem>().ToArrayAsync(Token));
        Assert.Empty(await read.Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostCommitResponseNeverAddsCompensatingAccounting(bool handlerFails)
    {
        string connection = await DatabaseAsync(postgres);
        var handler = new HandlerScenario { OnHandle = handlerFails ? FailAfterSaveAsync : null };
        var message = Message();
        await using (var intake = Provider(connection))
            await ReceiveAsync(intake, message);

        await using var provider = RecoveryProvider(
            connection,
            handler,
            new FailureScenario { Decide = _ => new InboxFailureDecision.DeadLetter("review") },
            new LostCommitResponse()
        );
        if (handlerFails)
        {
            var failure = await Assert.ThrowsAsync<InboxFailureAccountingException>(() =>
                ProcessAsync(provider)
            );
            Assert.Equal(message.MessageId, failure.MessageId);
            Assert.IsType<IOException>(failure.ProcessingFailure);
            Assert.IsType<IOException>(failure.AccountingFailure);
        }
        else
            await Assert.ThrowsAsync<IOException>(() => ProcessAsync(provider));

        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(1, row.Revision);
        Assert.Equal(handlerFails ? 1 : 0, row.FailureCount);
        Assert.Equal(handlerFails, row.DeadLetteredAt is not null);
        Assert.Equal(!handlerFails, row.ProcessedAt is not null);
        await using var read = Context(connection);
        Assert.Equal(handlerFails ? 0 : 1, await read.Set<HandledItem>().CountAsync(Token));
    }

    [Fact]
    public async Task RecoveryRejectsDirtyAndOwnedTransactionsAndPolicyRegistrationConflicts()
    {
        string connection = await DatabaseAsync(postgres);
        var services = Services(connection);
        services.AddInboxFailurePolicy<InboxConsumer, Policy>("render");
        Assert.Throws<InvalidOperationException>(() =>
            services.AddInboxFailurePolicy<InboxConsumer, Policy>("render")
        );
        await using var provider = RecoveryProvider(connection, new HandlerScenario());
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InboxConsumer>();
        var recovery = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        database.Add(new HandledItem { Id = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recovery.RetryAsync("render", "exports", Guid.NewGuid(), Guid.NewGuid(), 0, Token)
        );
        database.ChangeTracker.Clear();
        await using (var transaction = await database.Database.BeginTransactionAsync(Token))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                recovery.RetryAsync("render", "exports", Guid.NewGuid(), Guid.NewGuid(), 0, Token)
            );

        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                recovery.RetryAsync("render", "exports", Guid.NewGuid(), Guid.NewGuid(), 0, Token)
            );

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            recovery.RetryAsync("render", "exports", Guid.Empty, Guid.NewGuid(), 0, Token)
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            recovery.RetryAsync("render", "exports", Guid.NewGuid(), Guid.NewGuid(), -1, Token)
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            recovery.RetryAsync("render", "exports", Guid.NewGuid(), Guid.Empty, 0, Token)
        );
    }

    [Fact]
    public async Task TerminatedOldProcessorCannotOverwriteSuccessorsQuarantine()
    {
        string connection = await DatabaseAsync(postgres);
        var entered = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new HandlerScenario
        {
            OnHandle = async (database, message, token) =>
            {
                database.Add(new HandledItem { Id = message.MessageId, Value = 7 });
                await database.SaveChangesAsync(token);
                await using var command = database.Database.GetDbConnection().CreateCommand();
                command.Transaction = database.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = "SELECT pg_backend_pid()";
                entered.SetResult((int)(await command.ExecuteScalarAsync(token))!);
                await release.Task.WaitAsync(token);
                throw new IOException("obsolete attempt failed");
            },
        };
        await using var provider = RecoveryProvider(
            connection,
            handler,
            new FailureScenario { Decide = _ => new InboxFailureDecision.DeadLetter("new-attempt") }
        );
        var message = Message();
        await ReceiveAsync(provider, message);
        var old = ProcessAsync(provider);
        int pid = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        InboxMessageRecord? successor = null;
        try
        {
            await using var control = new NpgsqlConnection(connection);
            await control.OpenAsync(Token);
            await using var kill = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", control);
            kill.Parameters.AddWithValue("pid", pid);
            Assert.Equal(true, await kill.ExecuteScalarAsync(Token));
            handler.OnHandle = FailAfterSaveAsync;
            await Assert.ThrowsAsync<IOException>(() => ProcessAsync(provider));
            successor = await ReadAsync(connection, message.MessageId);
            Assert.NotNull(successor.DeadLetteredAt);
            Assert.Equal(1, successor.FailureCount);
        }
        finally
        {
            release.TrySetResult();
        }

        var obsolete = await Assert.ThrowsAsync<InboxFailureAccountingException>(() => old);
        Assert.Equal(message.MessageId, obsolete.MessageId);
        Assert.IsType<IOException>(obsolete.ProcessingFailure);
        var retained = await ReadAsync(connection, message.MessageId);
        Assert.Equal(successor!.Revision, retained.Revision);
        Assert.Equal(successor.AvailableAt, retained.AvailableAt);
        Assert.Equal(successor.DeadLetteredAt, retained.DeadLetteredAt);
        Assert.Equal("new-attempt", retained.LastFailureCode);
        Assert.Equal(1, retained.FailureCount);
        await using var read = Context(connection);
        Assert.Empty(await read.Set<HandledItem>().ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OperatorFailureOrLostCommitResponsePreservesConditionalRetrySemantics(
        bool committed
    )
    {
        string connection = await DatabaseAsync(postgres);
        var handler = new HandlerScenario { OnHandle = FailAfterSaveAsync };
        await using var provider = RecoveryProvider(
            connection,
            handler,
            new FailureScenario { Decide = _ => new InboxFailureDecision.DeadLetter("review") }
        );
        var message = Message();
        await ReceiveAsync(provider, message);
        await Assert.ThrowsAsync<IOException>(() => ProcessAsync(provider));
        var stopped = await ReadAsync(connection, message.MessageId);
        if (committed)
        {
            await using var uncertain = RecoveryProvider(
                connection,
                handler,
                interceptor: new LostCommitResponse()
            );
            await Assert.ThrowsAsync<IOException>(() =>
                RetryAsync(uncertain, message, stopped.ReceiptId, stopped.Revision)
            );
            Assert.Equal(
                InboxRetryResult.NotApplied,
                await RetryAsync(provider, message, stopped.ReceiptId, stopped.Revision)
            );
        }
        else
        {
            await SqlAsync(
                connection,
                "ALTER TABLE receiver.inbox ADD CONSTRAINT reject_redrive CHECK (redrive_count = 0)"
            );
            await Assert.ThrowsAsync<PostgresException>(() =>
                RetryAsync(provider, message, stopped.ReceiptId, stopped.Revision)
            );
            var unchanged = await ReadAsync(connection, message.MessageId);
            Assert.Equal(stopped.Revision, unchanged.Revision);
            Assert.Equal(stopped.DeadLetteredAt, unchanged.DeadLetteredAt);
            Assert.Equal(1, unchanged.FailuresSinceRedrive);
            Assert.Equal(0, unchanged.RedriveCount);
            await SqlAsync(connection, "ALTER TABLE receiver.inbox DROP CONSTRAINT reject_redrive");
            Assert.Equal(
                InboxRetryResult.Retried,
                await RetryAsync(provider, message, stopped.ReceiptId, stopped.Revision)
            );
        }

        var retried = await ReadAsync(connection, message.MessageId);
        Assert.Equal(stopped.Revision + 1, retried.Revision);
        Assert.Equal(1, retried.FailureCount);
        Assert.Equal(0, retried.FailuresSinceRedrive);
        Assert.Equal(1, retried.RedriveCount);
        Assert.Null(retried.DeadLetteredAt);
    }

    private sealed class RollbackFailure : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) => throw new IOException("savepoint rollback unavailable");
    }

    private sealed class LostCommitResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) => throw new IOException("simulated response loss after actual PostgreSQL commit");
    }
}
