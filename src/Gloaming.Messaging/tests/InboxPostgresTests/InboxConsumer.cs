using System.Collections.Concurrent;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace Gloaming.Messaging.InboxTests;

public sealed class InboxConsumer(DbContextOptions<InboxConsumer> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigurePostgresInbox("receiver", "inbox");
        modelBuilder.ConfigurePostgresOutbox("receiver", "outbox");
        modelBuilder.Entity<HandledItem>().ToTable("handled", "receiver").HasKey(row => row.Id);
        modelBuilder.Entity<SharedState>().ToTable("state", "receiver").HasKey(row => row.Id);
        modelBuilder.Entity<SharedState>().Property(row => row.Version).IsConcurrencyToken();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateInboxChanges();
        this.ValidateOutboxChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateInboxChanges();
        this.ValidateOutboxChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public sealed class HandledItem
{
    public Guid Id { get; set; }
    public int Value { get; set; }
}

public sealed class SharedState
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public int Value { get; set; }
}

public sealed class HandlerScenario
{
    public Func<InboxConsumer, IncomingMessage, CancellationToken, Task>? OnHandle { get; set; }
    public ConcurrentQueue<Guid> Contexts { get; } = new();
    public bool Reply { get; set; }
}

public sealed class LocalHandler(
    InboxConsumer database,
    HandlerScenario scenario,
    IOutbox<InboxConsumer> outbox
) : IInboxHandler<InboxConsumer>
{
    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        scenario.Contexts.Enqueue(database.ContextId.InstanceId);
        if (scenario.OnHandle is { } callback)
            await callback(database, message, cancellationToken);
        else
            database.Add(new HandledItem { Id = message.MessageId, Value = 7 });
        if (scenario.Reply)
            outbox.Enqueue(
                OutgoingMessage.FromPayload(
                    Guid.NewGuid(),
                    "replies",
                    "handled",
                    1,
                    new { message.MessageId },
                    new JsonSerializerOptions(),
                    message.TenantKey,
                    message.CorrelationId,
                    message.MessageId.ToString()
                )
            );
    }
}

internal static class InboxProof
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static InboxConsumer Context(string connection) =>
        new(new DbContextOptionsBuilder<InboxConsumer>().UseNpgsql(connection).Options);

    internal static IncomingMessage Message(
        Guid? id = null,
        string json = "{\"value\":1}",
        string producer = "exports"
    )
    {
        using var document = JsonDocument.Parse(json);
        return new(
            id ?? Guid.NewGuid(),
            producer,
            "exports.render",
            1,
            document.RootElement,
            "alpha",
            "conversation",
            "parent"
        );
    }

    internal static ServiceCollection Services(string connection, HandlerScenario? scenario = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(scenario ?? new HandlerScenario());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<InboxConsumer>(options => options.UseNpgsql(connection));
        services.AddPostgresInbox<InboxConsumer>();
        services.AddPostgresOutbox<InboxConsumer>();
        services.AddPostgresInboxProcessor<InboxConsumer>(new(TimeSpan.FromSeconds(10)));
        services.AddInboxHandler<InboxConsumer, LocalHandler>("render");
        return services;
    }

    internal static ServiceProvider Provider(string connection, HandlerScenario? scenario = null) =>
        Services(connection, scenario)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

    internal static async Task<string> DatabaseAsync(PostgreSqlFixture fixture)
    {
        string connection = await fixture.CreateDatabaseAsync(Token);
        await using var database = Context(connection);
        await database.Database.EnsureCreatedAsync(Token);
        return connection;
    }

    internal static async Task<InboxReceiveResult> ReceiveAsync(
        ServiceProvider provider,
        IncomingMessage message,
        string subscription = "render",
        bool commit = true
    )
    {
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InboxConsumer>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var result = await scope
            .ServiceProvider.GetRequiredService<IInbox<InboxConsumer>>()
            .ReceiveAsync(subscription, message, Token);
        if (commit)
            await transaction.CommitAsync(Token);
        return result;
    }

    internal static async Task<InboxProcessingResult> ProcessAsync(
        ServiceProvider provider,
        CancellationToken? token = null,
        string subscription = "render"
    )
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxProcessor<InboxConsumer>>()
            .ProcessNextAsync(subscription, token ?? Token);
    }

    internal static async Task SqlAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    internal static Task ReadyAsync(string connection) =>
        SqlAsync(
            connection,
            "UPDATE receiver.inbox SET available_at = clock_timestamp() - interval '1 second' WHERE processed_at IS NULL"
        );

    internal static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!await condition().WaitAsync(timeout.Token))
            await Task.Delay(10, timeout.Token);
    }
}
