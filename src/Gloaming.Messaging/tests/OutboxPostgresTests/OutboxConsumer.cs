using System.Collections.Concurrent;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gloaming.Messaging.Tests;

[CollectionDefinition("Messaging PostgreSQL")]
public sealed class MessagingPostgresProofs : ICollectionFixture<PostgreSqlFixture>;

public sealed class OutboxConsumer(DbContextOptions<OutboxConsumer> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigurePostgresOutbox("messages", "pending");
        var state = modelBuilder.Entity<BusinessState>();
        state.ToTable("business", "messages");
        state.HasKey(row => row.Id);
        state.Property(row => row.Version).IsConcurrencyToken();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateOutboxChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateOutboxChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public sealed class BusinessState
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public int Amount { get; set; }
}

public sealed class RecordingPublisher : IMessagePublisher
{
    public ConcurrentQueue<OutgoingMessage> Messages { get; } = new();
    public Func<OutgoingMessage, CancellationToken, Task>? OnPublish { get; set; }

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        Messages.Enqueue(message);
        if (OnPublish is { } callback)
            await callback(message, cancellationToken);
    }
}

internal static class OutboxProof
{
    private static readonly int[] PayloadItems = [1, 2];
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static OutboxConsumer Context(string connection) =>
        new(new DbContextOptionsBuilder<OutboxConsumer>().UseNpgsql(connection).Options);

    internal static OutgoingMessage Message(Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            "exports",
            "exports.render",
            2,
            JsonSerializer.SerializeToElement(
                new
                {
                    name = "résumé",
                    items = PayloadItems,
                    optional = (string?)null,
                }
            ),
            "tenant-alpha",
            "conversation",
            "cause"
        );

    internal static PostgresOutboxDispatcher<OutboxConsumer> Dispatcher(
        OutboxConsumer context,
        RecordingPublisher publisher,
        TimeSpan? retry = null
    ) => new(context, publisher, new(TimeSpan.FromSeconds(30), retry ?? TimeSpan.Zero));

    internal static async Task<string> DatabaseAsync(PostgreSqlFixture postgres)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var setup = Context(connection);
        await setup.Database.EnsureCreatedAsync(Token);
        return connection;
    }

    internal static async Task<OutgoingMessage> EnqueueAsync(
        string connection,
        OutgoingMessage? message = null
    )
    {
        message ??= Message();
        await using var writer = Context(connection);
        await using var transaction = await writer.Database.BeginTransactionAsync(Token);
        new EfOutbox<OutboxConsumer>(writer).Enqueue(message);
        await writer.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return message;
    }

    internal static async Task SqlAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    internal static Task ExpireAsync(string connection) =>
        SqlAsync(
            connection,
            "UPDATE messages.pending SET lease_until = clock_timestamp() - interval '1 second' WHERE lease_token IS NOT NULL"
        );
}
