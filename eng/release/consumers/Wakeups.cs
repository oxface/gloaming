using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
string supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Select disposable PostgreSQL credentials.");
string suffix = Guid.NewGuid().ToString("N");
string databaseName = "gloaming_wakeup_" + suffix;
string role = "notification_worker_" + suffix;
string password = Guid.NewGuid().ToString("N");
var settings = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(settings.ConnectionString);
await admin.OpenAsync(token);
await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin))
    await create.ExecuteNonQueryAsync(token);

settings.Database = databaseName;
var runtime = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
{
    Username = role,
    Password = password,
    Pooling = false,
    Multiplexing = false,
    Enlist = false,
};
Exception? failure = null;
try
{
    await using (
        var layout = new QueueDatabase(
            new DbContextOptionsBuilder<QueueDatabase>()
                .UseNpgsql(settings.ConnectionString)
                .Options
        )
    )
        await layout.Database.EnsureCreatedAsync(token);

    // This bounded adopter owns its initial fixture layout. Populated additive/old-writer migration
    // and reconnect supervision are separate native provider proofs, not inferred from EnsureCreated.
    await using (var migration = new NpgsqlConnection(settings.ConnectionString))
    {
        await migration.OpenAsync(token);
        await using var install = new NpgsqlCommand(
            $"""
            CREATE FUNCTION notify.hint() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER
            SET search_path = pg_catalog, pg_temp AS $$
            BEGIN
                IF TG_TABLE_NAME = 'inbox' THEN PERFORM pg_catalog.pg_notify('adopt_inbox', '');
                ELSE PERFORM pg_catalog.pg_notify('adopt_outbox', ''); END IF;
                RETURN NULL;
            END $$;
            REVOKE ALL ON FUNCTION notify.hint() FROM PUBLIC;
            CREATE TRIGGER hint AFTER INSERT ON notify.inbox FOR EACH STATEMENT EXECUTE FUNCTION notify.hint();
            CREATE TRIGGER hint AFTER INSERT ON notify.outbox FOR EACH STATEMENT EXECUTE FUNCTION notify.hint();
            CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT CONNECT ON DATABASE {databaseName} TO {role};
            GRANT USAGE ON SCHEMA notify TO {role};
            GRANT SELECT, INSERT, UPDATE ON notify.inbox, notify.outbox TO {role};
            GRANT SELECT, INSERT ON notify.handled TO {role};
            """,
            migration
        );
        await install.ExecuteNonQueryAsync(token);
    }

    var services = new ServiceCollection();
    services.AddSingleton<Published>();
    services.AddDbContext<QueueDatabase>(options => options.UseNpgsql(runtime.ConnectionString));
    services.AddPostgresInbox<QueueDatabase>();
    services.AddPostgresInboxProcessor<QueueDatabase>(new(TimeSpan.FromSeconds(5)));
    services.AddInboxHandler<QueueDatabase, Handler>("selected");
    services.AddPostgresOutbox<QueueDatabase>();
    services.AddPostgresOutboxDispatcher<QueueDatabase, Publisher>(
        new(TimeSpan.FromSeconds(5), TimeSpan.Zero)
    );
    await using var provider = services.BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
    );
    await StageAsync(Guid.NewGuid()); // Durable backlog exists before LISTEN registration.
    var incoming = Channel.CreateBounded<bool>(1);
    var outgoing = Channel.CreateBounded<bool>(1);
    await using var listener = new NpgsqlConnection(runtime.ConnectionString);
    listener.Notification += (_, notice) =>
    {
        if (notice.Channel == "adopt_inbox")
            incoming.Writer.TryWrite(true);
        else if (notice.Channel == "adopt_outbox")
            outgoing.Writer.TryWrite(true);
    };
    await listener.OpenAsync(token);
    await using (
        var register = new NpgsqlCommand("LISTEN adopt_inbox; LISTEN adopt_outbox", listener)
    )
        await register.ExecuteNonQueryAsync(token);

    Check(
        await PollInboxAsync() && await PollOutboxAsync(),
        "Fresh backlog poll follows committed LISTEN."
    );
    Check(!await PollInboxAsync() && !await PollOutboxAsync(), "Both typed queues become idle.");
    await StageAsync(Guid.NewGuid()); // Hints can precede the subsequent native wait.
    while (!incoming.Reader.TryPeek(out _) || !outgoing.Reader.TryPeek(out _))
        await listener.WaitAsync(1000, token);

    Check(
        incoming.Reader.TryRead(out _) && outgoing.Reader.TryRead(out _),
        "Independent bounded hints wake both callers."
    );
    Check(
        await PollInboxAsync() && await PollOutboxAsync(),
        "Only typed durable queue operations decide work."
    );
    await listener.CloseAsync();
    await StageAsync(Guid.NewGuid()); // Offline notifications are lost; a following poll still finds work.
    Check(
        await PollInboxAsync() && await PollOutboxAsync(),
        "No listener is needed for correctness."
    );
    Check(
        !await PollInboxAsync() && !await PollOutboxAsync(),
        "Repeated polls do not duplicate completion."
    );
    await using (var scope = provider.CreateAsyncScope())
    {
        var database = scope.ServiceProvider.GetRequiredService<QueueDatabase>();
        Check(
            await database.Set<Handled>().CountAsync(token) == 3
                && await database.Set<Handled>().AllAsync(x => x.Value == 11, token),
            "Independent expected handler effects survive."
        );
    }

    Check(
        provider.GetRequiredService<Published>().Ids.Count == 3
            && provider.GetRequiredService<Published>().Ids.Distinct().Count() == 3,
        "Three original messages publish once in this bounded rehearsal."
    );
    Console.WriteLine(
        "PASS wakeups: package-only typed inbox/outbox, restricted runtime, committed registration/backlog, retained per-caller hints and offline polling"
    );

    async Task StageAsync(Guid id)
    {
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<QueueDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await scope
            .ServiceProvider.GetRequiredService<IInbox<QueueDatabase>>()
            .ReceiveAsync(
                "selected",
                new(id, "owned", "work", 1, JsonSerializer.SerializeToElement(new { value = 11 })),
                token
            );
        scope
            .ServiceProvider.GetRequiredService<IOutbox<QueueDatabase>>()
            .Enqueue(
                new(
                    id,
                    "selected",
                    "work",
                    1,
                    JsonSerializer.SerializeToElement(new { value = 22 })
                )
            );
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    async Task<bool> PollInboxAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope
                .ServiceProvider.GetRequiredService<IInboxProcessor<QueueDatabase>>()
                .ProcessNextAsync("selected", token) != InboxProcessingResult.NoWork;
    }

    async Task<bool> PollOutboxAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope
                .ServiceProvider.GetRequiredService<IOutboxDispatcher<QueueDatabase>>()
                .DispatchNextAsync(token) != OutboxDispatchResult.NoWork;
    }
}
catch (Exception error)
{
    failure = error;
    throw;
}
finally
{
    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    try
    {
        await using (
            var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin)
        )
            await drop.ExecuteNonQueryAsync(cleanup.Token);

        await using var removeRole = new NpgsqlCommand($"DROP ROLE IF EXISTS {role}", admin);
        await removeRole.ExecuteNonQueryAsync(cleanup.Token);
    }
    catch (Exception error) when (failure is not null)
    {
        Console.Error.WriteLine($"Wakeup adopter cleanup failed ({error.GetType().Name}).");
    }
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class QueueDatabase(DbContextOptions<QueueDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigurePostgresInbox("notify", "inbox");
        modelBuilder.ConfigurePostgresOutbox("notify", "outbox");
        modelBuilder.Entity<Handled>().ToTable("handled", "notify").HasKey(x => x.Id);
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

sealed class Handled
{
    public Guid Id { get; set; }
    public int Value { get; set; }
}

sealed class Handler(QueueDatabase database) : IInboxHandler<QueueDatabase>
{
    public Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        if (message.MessageName != "work" || message.SchemaVersion != 1)
            throw new InvalidOperationException("Consumer owns accepted input contracts.");

        database.Add(
            new Handled
            {
                Id = message.MessageId,
                Value = message.Payload.GetProperty("value").GetInt32(),
            }
        );
        return Task.CompletedTask;
    }
}

sealed class Published
{
    internal ConcurrentQueue<Guid> Ids { get; } = new();
}

sealed class Publisher(Published published) : IMessagePublisher
{
    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        if (
            message.MessageName != "work"
            || message.SchemaVersion != 1
            || message.Payload.GetProperty("value").GetInt32() != 22
        )
            throw new InvalidOperationException("Consumer owns the outgoing wire reader.");

        published.Ids.Enqueue(message.MessageId);
        return Task.CompletedTask;
    }
}
