using System.Data.Common;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;

namespace Gloaming.Messaging.Tests;

internal static class OutboxRecoveryProof
{
    internal sealed class Policy(Func<OutboxFailure, OutboxFailureDecision> decide)
        : IOutboxFailurePolicy<OutboxConsumer>
    {
        public OutboxFailureDecision Decide(OutboxFailure failure) => decide(failure);
    }

    internal static readonly Policy Stop = new(_ => new OutboxFailureDecision.DeadLetter("review"));

    internal static async Task<OutboxMessageRecord> ReadAsync(string connection, Guid id)
    {
        await using var database = Context(connection);
        return await database
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(row => row.MessageId == id, Token);
    }

    internal static async Task<T> StoreAsync<T>(
        string connection,
        Func<IOutboxDeadLetters<OutboxConsumer>, Task<T>> call,
        DbTransactionInterceptor? interceptor = null
    )
    {
        var services = new ServiceCollection();
        services.AddDbContext<OutboxConsumer>(options =>
        {
            options.UseNpgsql(connection);
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
        });
        services.AddPostgresOutboxDeadLetters<OutboxConsumer>();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await using var scope = provider.CreateAsyncScope();
        return await call(
            scope.ServiceProvider.GetRequiredService<IOutboxDeadLetters<OutboxConsumer>>()
        );
    }

    internal static async Task FailAsync(
        string connection,
        IOutboxFailurePolicy<OutboxConsumer>? policy = null,
        RecordingPublisher? publisher = null
    )
    {
        publisher ??= new()
        {
            OnPublish = (_, _) => throw new IOException("publication observation"),
        };
        await using var database = Context(connection);
        await Assert.ThrowsAsync<IOException>(() =>
            new PostgresOutboxDispatcher<OutboxConsumer>(
                database,
                publisher,
                new(TimeSpan.FromSeconds(30), TimeSpan.Zero),
                failurePolicy: policy
            ).DispatchNextAsync(Token)
        );
    }

    internal static async Task<bool> ScalarAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        return await command.ExecuteScalarAsync(Token) is true;
    }

    internal static async Task WaitAsync(string connection, string observedCondition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!await ScalarAsync(connection, observedCondition))
            await Task.Delay(10, deadline.Token);
    }

    internal sealed class LostCommitResponse(int commitNumber) : DbTransactionInterceptor
    {
        private int commits;

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref commits) == commitNumber)
                throw new IOException("response lost after actual PostgreSQL commit");

            return Task.CompletedTask;
        }
    }
}
