using System.Transactions;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;
using static Gloaming.Messaging.Tests.OutboxRecoveryProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class BacklogTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task EmptyRouteIsAnObservedEmptySnapshotWithoutRequiringDispatcherRegistration()
    {
        string connection = await DatabaseAsync(postgres);
        await using var database = Context(connection);
        var snapshot = await database.ReadPostgresOutboxBacklogAsync("exports", Token);
        Assert.Equal(0, snapshot.Pending);
        Assert.Equal(0, snapshot.Eligible);
        Assert.Equal(0, snapshot.Delayed);
        Assert.Equal(0, snapshot.Leased);
        Assert.Equal(0, snapshot.DeadLetters);
        Assert.Null(snapshot.OldestEligibleAge);
        Assert.NotEqual(default, snapshot.ObservedAt);
    }

    [Fact]
    public async Task MixedStatePartitionsOneRouteAndAgesTheOldestEligibleOriginalInsertion()
    {
        string connection = await DatabaseAsync(postgres);
        var messages = new List<OutgoingMessage>();
        for (int i = 0; i < 7; i++)
            messages.Add(await EnqueueAsync(connection));

        await EnqueueAsync(
            connection,
            new(Guid.NewGuid(), "other", "render", 1, Message().Payload)
        );
        await SqlAsync(
            connection,
            $"""
            UPDATE messages.pending SET queued_at = clock_timestamp() - interval '10 minutes'
                WHERE message_id = '{messages[0].MessageId}';
            UPDATE messages.pending SET lease_token = gen_random_uuid(), lease_until = clock_timestamp() - interval '1 minute',
                attempts = 1, revision = 1 WHERE message_id = '{messages[1].MessageId}';
            UPDATE messages.pending SET available_at = clock_timestamp() + interval '1 hour'
                WHERE message_id IN ('{messages[2].MessageId}', '{messages[4].MessageId}');
            UPDATE messages.pending SET lease_token = gen_random_uuid(), lease_until = clock_timestamp() + interval '1 hour',
                attempts = 1, revision = 1 WHERE message_id IN ('{messages[
                3
            ].MessageId}', '{messages[4].MessageId}');
            UPDATE messages.pending SET attempts = 1, revision = 2, failure_count = 1, failures_since_redrive = 1,
                last_failed_at = clock_timestamp(), last_failure_code = 'review', dead_lettered_at = clock_timestamp()
                WHERE message_id = '{messages[5].MessageId}';
            UPDATE messages.pending SET attempts = 1, revision = 2, dispatched_at = clock_timestamp()
                WHERE message_id = '{messages[6].MessageId}';
            """
        );
        await using var database = Context(connection);
        var snapshot = await database.ReadPostgresOutboxBacklogAsync("exports", Token);
        Assert.Equal(5, snapshot.Pending);
        Assert.Equal(2, snapshot.Eligible);
        Assert.Equal(1, snapshot.Delayed);
        Assert.Equal(2, snapshot.Leased);
        Assert.Equal(1, snapshot.DeadLetters);
        Assert.Equal(
            snapshot.ObservedAt - (await ReadAsync(connection, messages[0].MessageId)).QueuedAt,
            snapshot.OldestEligibleAge
        );
        await ExpireAsync(connection);
        var expired = await database.ReadPostgresOutboxBacklogAsync("exports", Token);
        Assert.Equal(5, expired.Pending);
        Assert.Equal(3, expired.Eligible);
        Assert.Equal(2, expired.Delayed);
        Assert.Equal(0, expired.Leased);
        Assert.Equal(1, expired.DeadLetters);
        Assert.Equal(1, (await ReadAsync(connection, messages[1].MessageId)).Attempts);
        Assert.Equal(1, (await ReadAsync(connection, messages[1].MessageId)).Revision);
    }

    [Fact]
    public async Task UncommittedInsertsAndLockedChangesDoNotBlockOrBecomeVisibleBeforeCommit()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        await using var writer = Context(connection);
        await using var transaction = await writer.Database.BeginTransactionAsync(Token);
        await writer.Database.ExecuteSqlRawAsync(
            "UPDATE messages.pending SET available_at = clock_timestamp() + interval '1 hour'",
            Token
        );
        new EfOutbox<OutboxConsumer>(writer).Enqueue(Message());
        await writer.SaveChangesAsync(Token);
        await using var reader = Context(connection);
        var before = await reader
            .ReadPostgresOutboxBacklogAsync("exports", Token)
            .WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Equal(1, before.Pending);
        Assert.Equal(1, before.Eligible);
        Assert.Equal(0, before.Delayed);
        await transaction.CommitAsync(Token);
        var after = await reader.ReadPostgresOutboxBacklogAsync("exports", Token);
        Assert.Equal(2, after.Pending);
        Assert.Equal(1, after.Eligible);
        Assert.Equal(1, after.Delayed);
    }

    [Fact]
    public async Task MetadataOnlySelectRoleSeesOnlyRlsAdmittedRowsAndCannotReadPayloadOrWrite()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        await EnqueueAsync(
            connection,
            new(Guid.NewGuid(), "exports", "render", 1, Message().Payload, "tenant-beta")
        );
        string role = "backlog_" + Guid.NewGuid().ToString("N");
        await SqlAsync(
            connection,
            $"""
            CREATE ROLE "{role}" LOGIN PASSWORD 'diagnostic-proof';
            GRANT USAGE ON SCHEMA messages TO "{role}";
            GRANT SELECT (destination, dispatched_at, dead_lettered_at, available_at, lease_until, queued_at)
                ON messages.pending TO "{role}";
            ALTER TABLE messages.pending ENABLE ROW LEVEL SECURITY;
            CREATE POLICY admitted_backlog ON messages.pending TO "{role}" USING (owner_key = 'tenant-alpha');
            """
        );
        string restricted = new NpgsqlConnectionStringBuilder(connection)
        {
            Username = role,
            Password = "diagnostic-proof",
        }.ConnectionString;
        await using var database = Context(restricted);
        Assert.Equal(1, (await database.ReadPostgresOutboxBacklogAsync("exports", Token)).Pending);
        await using var denied = new NpgsqlConnection(restricted);
        await denied.OpenAsync(Token);
        foreach (
            string sql in new[]
            {
                "SELECT payload FROM messages.pending",
                "UPDATE messages.pending SET attempts = 1",
            }
        )
        {
            await using var command = new NpgsqlCommand(sql, denied);
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                command.ExecuteNonQueryAsync(Token)
            );
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    [Theory]
    [InlineData("tracked")]
    [InlineData("pending")]
    [InlineData("transaction")]
    [InlineData("ambient")]
    public async Task CallerStateCannotChangeTheIndependentObservationContract(string callerState)
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        await using var database = Context(connection);
        if (callerState == "tracked")
            await database.Set<OutboxMessageRecord>().SingleAsync(Token);

        if (callerState == "pending")
            database.Set<BusinessState>().Add(new() { Id = Guid.NewGuid() });

        await using var transaction =
            callerState == "transaction"
                ? await database.Database.BeginTransactionAsync(Token)
                : null;
        using var ambient =
            callerState == "ambient"
                ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)
                : null;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.ReadPostgresOutboxBacklogAsync("exports", Token)
        );
    }
}
