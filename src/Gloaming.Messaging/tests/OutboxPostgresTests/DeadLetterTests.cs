using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;
using static Gloaming.Messaging.Tests.OutboxRecoveryProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class DeadLetterTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task BoundedLivePagesUseDatabaseUuidOrderingExactRouteAndCodeAndSafeMetadata()
    {
        string connection = await DatabaseAsync(postgres);
        Guid[] ids =
        [
            Guid.Parse("80000000-0000-0000-0000-000000000000"),
            Guid.Parse("7fffffff-0000-0000-0000-000000000000"),
            Guid.Parse("00000001-0000-0000-0000-000000000000"),
            Guid.Parse("ffffffff-0000-0000-0000-000000000000"),
        ];
        foreach (var id in ids)
        {
            await EnqueueAsync(connection, Message(id));
            await FailAsync(connection, Stop);
        }

        var other = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "other-route",
            "other",
            1,
            new { value = 1 },
            new JsonSerializerOptions()
        );
        await EnqueueAsync(connection, other);
        await FailAsync(connection, Stop);
        await EnqueueAsync(connection);
        await SqlAsync(
            connection,
            "UPDATE messages.pending SET dead_lettered_at = '2026-10-21 00:00:00Z' WHERE dead_lettered_at IS NOT NULL"
        );

        Guid[] expected;
        await using (var native = new NpgsqlConnection(connection))
        {
            await native.OpenAsync(Token);
            await using var command = new NpgsqlCommand(
                "SELECT array_agg(message_id ORDER BY message_id) FROM messages.pending WHERE destination = 'exports' AND dead_lettered_at IS NOT NULL",
                native
            );
            expected = (Guid[])(await command.ExecuteScalarAsync(Token))!;
        }

        var first = await StoreAsync(
            connection,
            store => store.ListAsync("exports", new(PageSize: 2, FailureCode: "review"), Token)
        );
        Assert.Equal(expected.Take(2), first.Items.Select(row => row.MessageId));
        Assert.NotNull(first.NextCursor);
        var second = await StoreAsync(
            connection,
            store =>
                store.ListAsync(
                    "exports",
                    new(PageSize: 3, FailureCode: "review", After: first.NextCursor),
                    Token
                )
        );
        Assert.Equal(expected.Skip(2), second.Items.Select(row => row.MessageId));
        Assert.Null(second.NextCursor);
        Assert.Empty(
            (
                await StoreAsync(
                    connection,
                    store => store.ListAsync("exports", new(FailureCode: "Review"), Token)
                )
            ).Items
        );
        Assert.Single(
            (
                await StoreAsync(connection, store => store.ListAsync("other-route", new(), Token))
            ).Items
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            StoreAsync(
                connection,
                store => store.ListAsync("other-route", new(After: first.NextCursor), Token)
            )
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            StoreAsync(
                connection,
                store => store.ListAsync("exports", new(After: first.NextCursor), Token)
            )
        );
        var item = (
            await StoreAsync(connection, store => store.GetAsync("exports", expected[0], Token))
        )!;
        Assert.Equal(1, item.Attempts);
        Assert.Equal(1, item.FailureCount);
        var names = typeof(OutboxDeadLetter)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        foreach (
            string excluded in new[]
            {
                "Payload",
                "TenantKey",
                "TraceParent",
                "TraceState",
                "CorrelationId",
                "CausationId",
                "LeaseToken",
                "Exception",
            }
        )
            Assert.DoesNotContain(excluded, names);

        // Deleting no records: authorized retries make remaining items disappear from live paging.
        foreach (var row in second.Items)
            Assert.Equal(
                OutboxRetryResult.Retried,
                await StoreAsync(
                    connection,
                    store => store.RetryAsync("exports", row.MessageId, row.Revision, Token)
                )
            );
        Assert.Empty(
            (
                await StoreAsync(
                    connection,
                    store =>
                        store.ListAsync(
                            "exports",
                            new(FailureCode: "review", After: first.NextCursor),
                            Token
                        )
                )
            ).Items
        );
        Assert.Null(
            await StoreAsync(connection, store => store.GetAsync("exports", other.MessageId, Token))
        );
        Assert.Null(
            await StoreAsync(
                connection,
                store => store.GetAsync("exports", second.Items[0].MessageId, Token)
            )
        );
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(101, null, null)]
    [InlineData(1, " ", null)]
    [InlineData(1, null, "garbage")]
    public async Task InvalidQueriesRejectBeforeOpeningUnavailableStorage(
        int size,
        string? code,
        string? cursor
    )
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            StoreAsync(
                "Host=unavailable;Database=unused;Username=unused",
                store => store.ListAsync("exports", new(size, code, cursor), Token)
            )
        );
    }

    [Fact]
    public async Task FreshContextGuardAndRequestedCancellationDoNotOpenStorage()
    {
        var services = new ServiceCollection();
        services.AddDbContext<OutboxConsumer>(options =>
            options.UseNpgsql("Host=unavailable;Database=unused;Username=unused")
        );
        services.AddPostgresOutboxDeadLetters<OutboxConsumer>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<OutboxConsumer>();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxDeadLetters<OutboxConsumer>>();
        database.Attach(new BusinessState { Id = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ListAsync("exports", new(), Token)
        );
        database.ChangeTracker.Clear();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.ListAsync("exports", new(), cancelled.Token)
        );
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            store.RetryAsync("exports", Guid.Empty, 0, Token)
        );
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            store.RetryAsync("exports", Guid.NewGuid(), -1, Token)
        );
    }

    [Fact]
    public async Task CompetingOperatorsWaitRecheckAndApplyOnceWhileInspectionRemainsLive()
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        await FailAsync(connection, Stop);
        var stopped = (
            await StoreAsync(
                connection,
                store => store.GetAsync("exports", message.MessageId, Token)
            )
        )!;
        await using var owner = new NpgsqlConnection(connection);
        await owner.OpenAsync(Token);
        await using var transaction = await owner.BeginTransactionAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                "SELECT message_id FROM messages.pending FOR UPDATE",
                owner,
                transaction
            )
        )
            await command.ExecuteScalarAsync(Token);
        string operatorConnection = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "competing-operators",
        }.ConnectionString;
        var first = StoreAsync(
            operatorConnection,
            store => store.RetryAsync("exports", message.MessageId, stopped.Revision, Token)
        );
        var second = StoreAsync(
            operatorConnection,
            store => store.RetryAsync("exports", message.MessageId, stopped.Revision, Token)
        );
        try
        {
            await WaitAsync(
                connection,
                "SELECT count(*) = 2 FROM pg_stat_activity WHERE application_name = 'competing-operators' AND cardinality(pg_blocking_pids(pid)) > 0"
            );
            Assert.Equal(
                stopped,
                await StoreAsync(
                    connection,
                    store => store.GetAsync("exports", message.MessageId, Token)
                )
            );
            await transaction.CommitAsync(Token);
            var results = await Task.WhenAll(first, second);
            Assert.Single(results, result => result == OutboxRetryResult.Retried);
            Assert.Single(results, result => result == OutboxRetryResult.NotApplied);
        }
        finally
        {
            await transaction.DisposeAsync();
        }

        await FailAsync(connection, Stop);
        Assert.Equal(
            OutboxRetryResult.NotApplied,
            await StoreAsync(
                connection,
                store => store.RetryAsync("exports", message.MessageId, stopped.Revision, Token)
            )
        );
        var again = await ReadAsync(connection, message.MessageId);
        Assert.NotNull(again.DeadLetteredAt);
        Assert.Equal(1, again.RedriveCount);
    }

    [Fact]
    public async Task LostOperatorCommitResponseCannotRepeatTheTransition()
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        await FailAsync(connection, Stop);
        var stopped = await ReadAsync(connection, message.MessageId);
        await Assert.ThrowsAsync<IOException>(() =>
            StoreAsync(
                connection,
                store => store.RetryAsync("exports", message.MessageId, stopped.Revision, Token),
                new LostCommitResponse(1)
            )
        );
        Assert.Equal(
            OutboxRetryResult.NotApplied,
            await StoreAsync(
                connection,
                store => store.RetryAsync("exports", message.MessageId, stopped.Revision, Token)
            )
        );
        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(1, row.RedriveCount);
        Assert.Equal(stopped.FailureCount, row.FailureCount);
        Assert.Null(row.DeadLetteredAt);
    }

    [Fact]
    public async Task SelectOnlyCredentialCanInspectButCannotRetry()
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        await FailAsync(connection, Stop);
        string login = "inspect_" + Guid.NewGuid().ToString("N");
        await SqlAsync(
            connection,
            $"CREATE ROLE {login} LOGIN PASSWORD 'proof-only' NOSUPERUSER NOBYPASSRLS; GRANT USAGE ON SCHEMA messages TO {login}; GRANT SELECT ON messages.pending TO {login}"
        );
        string readOnly = new NpgsqlConnectionStringBuilder(connection)
        {
            Username = login,
            Password = "proof-only",
        }.ConnectionString;
        var item = (
            await StoreAsync(readOnly, store => store.GetAsync("exports", message.MessageId, Token))
        )!;
        Assert.Single(
            (await StoreAsync(readOnly, store => store.ListAsync("exports", new(), Token))).Items
        );
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            StoreAsync(
                readOnly,
                store => store.RetryAsync("exports", message.MessageId, item.Revision, Token)
            )
        );
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        Assert.Equal(item.Revision, (await ReadAsync(connection, message.MessageId)).Revision);

        string operatorLogin = "operator_" + Guid.NewGuid().ToString("N");
        await SqlAsync(
            connection,
            $"""
            CREATE ROLE {operatorLogin} LOGIN PASSWORD 'proof-operator' NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA messages TO {operatorLogin};
            GRANT SELECT (message_id, destination, message_name, schema_version, revision, attempts,
                failure_count, failures_since_redrive, queued_at, dead_lettered_at, last_failure_code,
                last_failed_at, redrive_count, last_redriven_at, dispatched_at, lease_token, lease_until)
                ON messages.pending TO {operatorLogin};
            GRANT UPDATE (dead_lettered_at, available_at, failures_since_redrive, revision,
                redrive_count, last_redriven_at) ON messages.pending TO {operatorLogin};
            """
        );
        string authorized = new NpgsqlConnectionStringBuilder(connection)
        {
            Username = operatorLogin,
            Password = "proof-operator",
        }.ConnectionString;
        Assert.Single(
            (await StoreAsync(authorized, store => store.ListAsync("exports", new(), Token))).Items
        );
        Assert.Equal(
            OutboxRetryResult.Retried,
            await StoreAsync(
                authorized,
                store => store.RetryAsync("exports", message.MessageId, item.Revision, Token)
            )
        );
        await using var operatorConnection = new NpgsqlConnection(authorized);
        await operatorConnection.OpenAsync(Token);
        await using var forbidden = new NpgsqlCommand(
            "SELECT payload FROM messages.pending",
            operatorConnection
        );
        Assert.Equal(
            PostgresErrorCodes.InsufficientPrivilege,
            (
                await Assert.ThrowsAsync<PostgresException>(() =>
                    forbidden.ExecuteScalarAsync(Token)
                )
            ).SqlState
        );
    }
}
